using System.Diagnostics;
using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.ReferenceServer;

public sealed record LocalEngineOutcome(byte[]? Png = null, JobFailure? Failure = null, bool Cancelled = false);
public interface ILocalEngine
{
    ModelCapabilities Model { get; }
    Task<LocalEngineOutcome> EditAsync(Guid id, string directory, JobSubmission submission, Func<bool> cancellationRequested, CancellationToken stopping);
}

// Paths come only from the user's private local configuration, never HTTP data.
public sealed record ManagedEngineConfiguration(string Python, string Script, string Installation, string ModelDirectory);

public sealed class ManagedEngine : ILocalEngine
{
    private readonly ManagedEngineConfiguration configuration;
    private readonly string modelDigest;
    public ModelCapabilities Model { get; }
    private ManagedEngine(ManagedEngineConfiguration configuration, string backend, string digest)
    {
        this.configuration = configuration;
        modelDigest = digest;
        Model = ManagedModel.Create(backend + "-" + digest[..16]);
    }

    public static ManagedEngine? FromConfiguration(string? path)
    {
        if (path is null) return null;
        try
        {
            if (!PrivateStorage.IsRegularFile(path) || new FileInfo(path).Length > 16384) return null;
            var config = SubmissionValidator.Deserialize<ManagedEngineConfiguration>(File.ReadAllBytes(path));
            foreach (string local in new[] { config.Python, config.Script, config.Installation, config.ModelDirectory })
                if (!Path.IsPathFullyQualified(local) || local.StartsWith("\\\\", StringComparison.Ordinal)) return null;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var process = Start(config, "check");
            string text;
            try
            {
                text = ReadOutput(process, deadline.Token).GetAwaiter().GetResult();
                process.WaitForExitAsync(deadline.Token).GetAwaiter().GetResult();
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            if (process.ExitCode != 0) return null;
            using var document = JsonDocument.Parse(text);
            var check = document.RootElement;
            if (!check.GetProperty("available").GetBoolean() || check.GetProperty("acceptance").GetString() != "nativeEditPassed" ||
                check.GetProperty("modelId").GetString() != ManagedModel.Id) return null;
            string digest = check.GetProperty("modelDigest").GetString()!;
            string backend = check.GetProperty("backend").GetString()!;
            if (digest.Length != 64 || !digest.All(Uri.IsHexDigit) || backend.Length > 80) return null;
            return new(config, backend, digest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or
            OperationCanceledException or System.ComponentModel.Win32Exception or KeyNotFoundException or ArgumentException)
        { return null; } // No model is advertised when preparation/runtime checks fail.
    }

    public async Task<LocalEngineOutcome> EditAsync(Guid id, string directory, JobSubmission submission, Func<bool> cancellationRequested, CancellationToken stopping)
    {
        string stateRoot = Path.Combine(Path.GetDirectoryName(directory)!, "engine-jobs");
        PrivateStorage.CreateDirectory(stateRoot);
        PrivateStorage.WriteBytes(Path.Combine(directory, "prompt.txt"), System.Text.Encoding.UTF8.GetBytes(submission.Intent.EffectivePrompt));
        using var process = Start(configuration, "edit", "--job-id", id.ToString("D"), "--reference", Path.Combine(directory, "reference.png"),
            "--prompt", Path.Combine(directory, "prompt.txt"), "--state-directory", stateRoot);
        try
        {
            var output = ReadOutput(process, stopping);
            bool requested = false;
            while (!process.HasExited)
            {
                if (output.IsFaulted) await output;
                if (!requested && cancellationRequested())
                {
                    using var cancel = Start(configuration, "cancel", "--job-id", id.ToString("D"), "--state-directory", stateRoot);
                    await cancel.WaitForExitAsync(stopping);
                    requested = cancel.ExitCode == 0; // Job creation may still be in progress.
                }
                await Task.Delay(100, stopping);
            }
            string text = await output;
            using var state = JsonDocument.Parse(text);
            var record = state.RootElement;
            string? status = record.TryGetProperty("state", out var value) ? value.GetString() : null;
            if (status == "cancelled") return new(Cancelled: true);
            if (status != "completed")
            {
                string code = record.TryGetProperty("failure", out var error) ? error.GetString() ?? "engineFailed" : "engineFailed";
                if (code.Length > 80 || !code.All(char.IsAsciiLetterOrDigit)) code = "engineFailed";
                return new(Failure: new(code, "The local engine did not complete the edit. Check the prepared model in Settings."));
            }
            string path = Path.Combine(stateRoot, id.ToString("D"), "result.png");
            if (record.GetProperty("modelDigest").GetString() != modelDigest ||
                record.GetProperty("sourceSha256").GetString() != submission.PreparedReference!.Sha256 ||
                !PrivateStorage.IsRegularFile(path) || new FileInfo(path).Length > 16_777_216)
                return new(Failure: new("invalidResult", "The local output does not match this job."));
            byte[] png = await File.ReadAllBytesAsync(path, stopping);
            if (ContentDigest.Sha256(png) != record.GetProperty("outputSha256").GetString() || PngHeader.Read(png) != submission.Plan.Native)
                return new(Failure: new("invalidResult", "The local output failed frame/hash validation."));
            return new(png);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    private static Process Start(ManagedEngineConfiguration config, string action, params string[] arguments)
    {
        var start = new ProcessStartInfo(config.Python) { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true };
        var permitted = start.Environment.Where(p => p.Key.ToUpperInvariant() is "PATH" or "SYSTEMROOT" or "WINDIR" or "TEMP" or "TMP" or "LOCALAPPDATA" or "USERPROFILE" or "LANG").ToArray();
        start.Environment.Clear();
        foreach (var pair in permitted) start.Environment[pair.Key] = pair.Value;
        start.ArgumentList.Add("-I"); start.ArgumentList.Add(config.Script); start.ArgumentList.Add(action);
        start.ArgumentList.Add("--installation"); start.ArgumentList.Add(config.Installation);
        start.ArgumentList.Add("--model-directory"); start.ArgumentList.Add(config.ModelDirectory);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        var process = Process.Start(start) ?? throw new IOException("Could not start the local engine.");
        // Discard library messages; private Python failure records own diagnostics.
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task<string> ReadOutput(Process process, CancellationToken ct)
    {
        var text = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await process.StandardOutput.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (text.Length + count > 65536) throw new IOException("Engine output exceeded its limit.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}

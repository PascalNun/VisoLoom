using System.Diagnostics;
using System.Text;
using System.Text.Json;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

public sealed record ReviewedModelPackage(string Id, string Display, string Purpose, string Terms, string Profile, long TotalBytes, int FileCount, bool RequiresToken = false);
public sealed record ReviewedInstallPlan(string Id, long TotalBytes, long FreeBytes, bool Ready, string Reason);
public sealed record ReviewedModelProgress(string Name, int Index, int FileCount, long Bytes);
public sealed record RegisteredInstallation(string Id, string Profile, string Destination, string Receipt, string Digest, long TotalBytes, int Files, string InstalledAt);
public sealed record ReviewedInstallResult(string[] Installed, string Destination, string Digest);
public sealed record ReviewedUninstall(string Removed, string Destination);

// Reviewed downloads run through the engine's pinned installer: pinned HTTPS
// sources, streamed SHA-256 verification, register-only-complete packages. The
// bridge starts the process and relays its reports; it never invents packages,
// paths or progress, and cancellation leaves staging cleanup engine-owned.
public sealed class LocalModelInstaller
{
    private const int BoundedSeconds = 15;
    internal const int MaximumReportedBytes = 131072;
    internal const int MaximumMessageBytes = 512;
    private readonly string connection;

    public LocalModelInstaller(string? connectionFile = null) => connection = connectionFile ?? EngineRuntime.DefaultConnectionPath();

    // Installation state lives in the engine's own per-user root beside the library.
    public string StateDirectory => Path.GetDirectoryName(EngineRuntime.LocalPath(connection))!;
    public string ReceiptPath(string packageId) => Path.Combine(StateDirectory, "installs", packageId + ".json");

    public async Task<IReadOnlyList<ReviewedModelPackage>> ListPackagesAsync(CancellationToken ct = default)
        => await RunBoundedAsync<ReviewedModelPackage[]>("list", [], ct).ConfigureAwait(false) ?? [];
    public Task<ReviewedInstallPlan> PlanAsync(string packageId, string destination, CancellationToken ct = default)
        => RunBoundedAsync<ReviewedInstallPlan>("plan",
            ["--package", packageId, "--destination", EngineRuntime.LocalPath(destination)], ct);
    public async Task<IReadOnlyList<RegisteredInstallation>> InstallsAsync(CancellationToken ct = default)
        => await RunBoundedAsync<RegisteredInstallation[]>("installs", ["--state-directory", StateDirectory], ct)
            .ConfigureAwait(false) ?? [];
    public Task<ReviewedUninstall> UninstallAsync(string destination, CancellationToken ct = default)
        => RunBoundedAsync<ReviewedUninstall>("uninstall",
            ["--destination", EngineRuntime.LocalPath(destination), "--state-directory", StateDirectory], ct);

    // One background process, one JSON object per line: progress per completed
    // file, the final record last. There is no overall timeout — a download
    // takes as long as it takes — while cancellation stops the whole process
    // tree and the engine cleans its own staging. The download token travels
    // over standard input once and the stream closes behind it: argv, the
    // environment and every file stay free of it, and the engine reads it
    // only when an authenticated pin actually needs it (ADR 0032).
    public async Task<ReviewedInstallResult> InstallAsync(string packageId, string destination, string? receipt = null,
        Action<ReviewedModelProgress>? onProgress = null, CancellationToken ct = default, string? bearerToken = null)
    {
        EngineRuntime.LocalPath(destination);
        var receiptPath = EngineRuntime.LocalPath(receipt ?? ReceiptPath(packageId));
        var runtime = await EngineRuntime.ResolveAsync(connection, "model_install.py", ct).ConfigureAwait(false);
        var start = runtime.Start();
        start.RedirectStandardInput = true;
        foreach (string item in new[] { "-I", runtime.Helper, "install", "--progress", "--package", packageId,
                   "--destination", destination, "--receipt", receiptPath, "--state-directory", StateDirectory })
            start.ArgumentList.Add(item);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the model installer.");
        if (bearerToken is { Length: > 0 })
        {
            var writer = process.StandardInput;
            await writer.WriteLineAsync(bearerToken.AsMemory(), ct).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
            writer.Close();
        }
        else
        {
            // No token saved: an authenticated package fails closed in the
            // engine with its own message instead of reading anything here.
            process.StandardInput.Close();
        }
        var reported = new StringBuilder();
        void Collect(string line)
        {
            if (reported.Length + line.Length <= MaximumReportedBytes) reported.AppendLine(line);
        }
        process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) Collect(line); };
        process.BeginErrorReadLine();
        ReviewedInstallResult? final = null;
        try
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                if (line.Length == 0) continue;
                if (line.Length > MaximumReportedBytes) throw new IOException("The installer report exceeded its limit.");
                if (ProgressFromLine(line) is { } progress) { onProgress?.Invoke(progress); continue; }
                if (ResultFromLine(line) is { } done) { final = done; continue; }
                throw new IOException("The installer reported an unrecognized result. The run stopped before anything is kept.");
            }
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw FailureFromReport(reported.ToString(), "installerUnavailable",
                    "The model download stopped before finishing. Nothing was kept.");
            return final ?? throw new IOException("The model download finished without its result record. Check the model list before retrying.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    // The two fixed report shapes the engine emits, parsed fail-closed so the
    // bridge is a relay, not an interpreter.
    internal static ReviewedModelProgress? ProgressFromLine(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("event", out var kind)
            && kind.GetString() == "fileComplete"
            ? root.Deserialize<ReviewedModelProgress>(WireJson.Options) : null;
    }

    internal static ReviewedInstallResult? ResultFromLine(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        return root.ValueKind == JsonValueKind.Object && !root.TryGetProperty("event", out _)
            && root.TryGetProperty("installed", out _)
            ? root.Deserialize<ReviewedInstallResult>(WireJson.Options) : null;
    }

    // Engine failures carry their own honest reason; an unreadable report gets
    // the fail-closed fallback instead of a guessed diagnosis.
    internal static ProviderException FailureFromReport(string? reported, string fallbackCode, string fallbackMessage)
    {
        try
        {
            using var doc = JsonDocument.Parse(reported?.Trim() ?? "");
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var code)
                && root.TryGetProperty("message", out var message)
                && code.GetString() is { Length: > 0 } error && message.GetString() is { } text)
                return new ProviderException(error, text.Length > MaximumMessageBytes ? text[..MaximumMessageBytes] : text);
        }
        catch (JsonException) { }
        return new ProviderException(fallbackCode, fallbackMessage);
    }

    private async Task<T> RunBoundedAsync<T>(string command, string[] arguments, CancellationToken ct)
    {
        var runtime = await EngineRuntime.ResolveAsync(connection, "model_install.py", ct).ConfigureAwait(false);
        var start = runtime.Start();
        foreach (string item in new[] { "-I", runtime.Helper, command }.Concat(arguments))
            start.ArgumentList.Add(item);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(BoundedSeconds));
        using var process = Process.Start(start) ?? throw new IOException("Could not start the model installer.");
        var reported = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { } line && reported.Length + line.Length <= MaximumReportedBytes) reported.AppendLine(line);
        };
        process.BeginErrorReadLine();
        try
        {
            var output = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await process.StandardOutput.ReadAsync(buffer.AsMemory(), deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (output.Length + count > MaximumReportedBytes) throw new IOException("The installer response exceeded its limit.");
                output.Append(buffer, 0, count);
            }
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw FailureFromReport(reported.ToString(), "installerUnavailable", "Could not read the reviewed packages.");
            using var result = JsonDocument.Parse(output.ToString());
            return result.RootElement.Deserialize<T>(WireJson.Options) ?? throw new IOException("Invalid installer response.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}

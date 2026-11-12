using System.Diagnostics;
using System.Text;
using System.Text.Json;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

public sealed record LocalModelFolder
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Directory { get; init; }
    public required string Status { get; init; }
    public required string Message { get; init; }
    public bool Candidate { get; init; }
    public bool Configured { get; init; }
    public string? Readiness { get; init; }
    public string? ReadinessMessage { get; init; }
    public long Bytes { get; init; }
    public int FileCount { get; init; }
}

public sealed record LocalModelSetupStep(string Name, string State, string Message);
public sealed record LocalModelSetupPlan(string Id, string State, string Message, bool ModelAccepted,
    long SourceBytes, long EstimatedPreparedBytes, long MinimumPreparationFreeBytes,
    long? PreparationFreeBytes, LocalModelSetupStep[] Steps);

// This is a local desktop setup tool, not an HTTP operation. Only the user's
// private runtime configuration supplies executable/script paths.
public sealed class LocalModelLibrary
{
    private sealed record Configuration(string Python, string Script);
    private sealed record Inventory(LocalModelFolder[] Models);
    private readonly string connection;
    public LocalModelLibrary(string? connectionFile = null) => connection = connectionFile ?? Path.Combine(
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
        "VisoLoom", "engine-prototype", "connection.json");

    public async Task<IReadOnlyList<LocalModelFolder>> ListAsync(CancellationToken ct = default)
        => (await RunAsync<Inventory>("list", [], ct).ConfigureAwait(false)).Models;
    public Task<LocalModelFolder> InspectAsync(string directory, CancellationToken ct = default)
        => RunAsync<LocalModelFolder>("inspect", ["--directory", LocalPath(directory)], ct);
    public Task<LocalModelFolder> AddAsync(string directory, CancellationToken ct = default)
        => RunAsync<LocalModelFolder>("add", ["--directory", LocalPath(directory)], ct);
    public Task<LocalModelSetupPlan> PlanAsync(string id, CancellationToken ct = default)
    {
        ValidateId(id);
        return RunAsync<LocalModelSetupPlan>("plan", ["--id", id], ct);
    }
    public async Task RemoveAsync(string id, CancellationToken ct = default)
    {
        ValidateId(id);
        await RunAsync<JsonElement>("remove", ["--id", id], ct).ConfigureAwait(false);
    }

    private static void ValidateId(string id)
    {
        if (id is null || id.Length != 24 || !id.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid model library entry.");
    }

    private async Task<T> RunAsync<T>(string action, string[] arguments, CancellationToken ct)
    {
        var path = LocalPath(connection);
        if (!File.Exists(path) || new FileInfo(path).Length > 16384)
            throw new ProviderException("runtimeMissing", "Local setup is required to check model files. See Connections in Settings. Automatic installation is planned.");
        var config = JsonSerializer.Deserialize<Configuration>(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false), WireJson.Options)
            ?? throw new IOException("Invalid local runtime configuration.");
        string python = LocalPath(config.Python), script = LocalPath(config.Script);
        string helper = LocalPath(Path.Combine(Path.GetDirectoryName(script)!, "model_library.py"));
        if (!File.Exists(python) || !File.Exists(helper))
            throw new ProviderException("runtimeMissing", "The local setup tools are unavailable. See Local setup in Connections.");
        var start = new ProcessStartInfo(python) { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true };
        var permitted = start.Environment.Where(p => p.Key.ToUpperInvariant() is "PATH" or "SYSTEMROOT" or "WINDIR" or "TEMP" or "TMP" or "LOCALAPPDATA" or "USERPROFILE" or "LANG").ToArray();
        start.Environment.Clear();
        foreach (var pair in permitted) start.Environment[pair.Key] = pair.Value;
        foreach (string item in new[] { "-I", helper, action, "--connection", path, "--library", Path.Combine(Path.GetDirectoryName(path)!, "models.json") }.Concat(arguments))
            start.ArgumentList.Add(item);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = Process.Start(start) ?? throw new IOException("Could not start local model inspection.");
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        try
        {
            var output = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await process.StandardOutput.ReadAsync(buffer.AsMemory(), deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (output.Length + count > 131072) throw new IOException("The model library response exceeded its limit.");
                output.Append(buffer, 0, count);
            }
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            using var result = JsonDocument.Parse(output.ToString());
            if (process.ExitCode != 0)
                throw new ProviderException("modelLibraryUnavailable", result.RootElement.GetProperty("message").GetString() ?? "Could not inspect this folder.");
            return result.RootElement.Deserialize<T>(WireJson.Options) ?? throw new IOException("Invalid model library response.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static string LocalPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal))
            throw new ArgumentException("Choose an absolute path on a local disk.");
        return path;
    }
}

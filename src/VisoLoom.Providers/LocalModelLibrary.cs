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
    private sealed record Inventory(LocalModelFolder[] Models);
    private readonly string connection;
    public LocalModelLibrary(string? connectionFile = null) => connection = connectionFile ?? EngineRuntime.DefaultConnectionPath();

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
        var path = EngineRuntime.LocalPath(connection);
        var runtime = await EngineRuntime.ResolveAsync(connection, "model_library.py", ct).ConfigureAwait(false);
        var start = runtime.Start();
        foreach (string item in new[] { "-I", runtime.Helper, action, "--connection", path, "--library", Path.Combine(runtime.StateDirectory, "models.json") }.Concat(arguments))
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

    private static string LocalPath(string path) => EngineRuntime.LocalPath(path);
}

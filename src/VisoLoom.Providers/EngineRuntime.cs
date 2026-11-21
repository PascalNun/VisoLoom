using System.Diagnostics;
using System.Text.Json;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

// The engine prototype's private runtime configuration is the only supplier of
// executable and script paths. Nothing here installs or downloads a runtime.
internal sealed record EngineRuntime(string Python, string Helper, string StateDirectory)
{
    private sealed record Configuration(string Python, string Script);

    public static string DefaultConnectionPath() => Path.Combine(
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
        "VisoLoom", "engine-prototype", "connection.json");

    public static async Task<EngineRuntime> ResolveAsync(string connection, string helperName, CancellationToken ct)
    {
        var path = LocalPath(connection);
        if (!File.Exists(path) || new FileInfo(path).Length > 16384)
            throw new ProviderException("runtimeMissing", "Local setup is required to check model files. See Connections in Settings. Automatic installation is planned.");
        var config = JsonSerializer.Deserialize<Configuration>(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false), WireJson.Options)
            ?? throw new IOException("Invalid local runtime configuration.");
        string python = LocalPath(config.Python);
        string helper = LocalPath(Path.Combine(Path.GetDirectoryName(LocalPath(config.Script))!, helperName));
        if (!File.Exists(python) || !File.Exists(helper))
            throw new ProviderException("runtimeMissing", "The local setup tools are unavailable. See Local setup in Connections.");
        return new EngineRuntime(python, helper, Path.GetDirectoryName(path)!);
    }

    public ProcessStartInfo Start()
    {
        var start = new ProcessStartInfo(Python)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        // Only the platform's core variables travel to the isolated helper;
        // the user's private environment does not.
        var permitted = start.Environment.Where(p => p.Key.ToUpperInvariant() is "PATH" or "SYSTEMROOT" or "WINDIR"
            or "TEMP" or "TMP" or "LOCALAPPDATA" or "USERPROFILE" or "LANG").ToArray();
        start.Environment.Clear();
        foreach (var pair in permitted) start.Environment[pair.Key] = pair.Value;
        return start;
    }

    public static string LocalPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Choose an absolute path on a local disk.");
        return path;
    }
}

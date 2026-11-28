using System.Diagnostics;
using System.Security.Cryptography;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

/// <summary>The loopback service started by this Rhino session. Engine weights stay on disk until a render.</summary>
public static class LocalEngineHost
{
    private const int Port = LocalDevelopmentHandshake.DefaultPort;
    private static readonly SemaphoreSlim Startup = new(1, 1);
    private static readonly string HostFolder = Path.Combine(
        Path.GetDirectoryName(typeof(LocalEngineHost).Assembly.Location) ?? "", "engine-host");
    private static Session? current;
    private sealed record Session(Process Process, LocalDevelopmentCredentials Credentials);

    public static bool CanStart => File.Exists(Path.Combine(HostFolder, "dotnet-path")) &&
        File.Exists(Path.Combine(HostFolder, "VisoLoom.ReferenceServer.dll"));

    public static Task<LocalDevelopmentCredentials?> FindAsync(CancellationToken ct) =>
        Volatile.Read(ref current) is { } session && IsRunning(session.Process)
            ? Task.FromResult<LocalDevelopmentCredentials?>(session.Credentials)
            : LocalDevelopmentConnection.FindAsync(ct);

    public static async Task<LocalDevelopmentCredentials> StartAsync(CancellationToken ct)
    {
        await Startup.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref current) is { } running && IsRunning(running.Process))
                return running.Credentials;
            Interlocked.Exchange(ref current, null)?.Process.Dispose();

            if (!CanStart)
                throw new ProviderException("runtimeMissing", "Local engine setup is required. Install the engine or start an existing local service.");
            string connection = Environment.GetEnvironmentVariable("VISOLOOM_ENGINE_CONFIG") ?? EngineRuntime.DefaultConnectionPath();
            if (!File.Exists(connection))
                throw new ProviderException("modelSetupRequired", "Set up an installed model in Local models first.");

            string dotnet = EngineRuntime.LocalPath((await File.ReadAllTextAsync(Path.Combine(HostFolder, "dotnet-path"), ct)
                .ConfigureAwait(false)).Trim());
            if (!File.Exists(dotnet))
                throw new ProviderException("runtimeMissing", "The local engine runtime is missing. Reinstall the engine to continue.");

            byte[] payload = LocalDevelopmentHandshake.Encode(Port, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            var credentials = LocalDevelopmentHandshake.Decode(payload);
            Array.Clear(payload);
            var launch = new ProcessStartInfo(dotnet)
            {
                WorkingDirectory = HostFolder, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            launch.ArgumentList.Add(Path.Combine(HostFolder, "VisoLoom.ReferenceServer.dll"));
            foreach (string key in launch.Environment.Keys.ToArray())
                if (key.ToUpperInvariant() is not ("SYSTEMROOT" or "WINDIR" or "PATH" or "TEMP" or "TMP" or "TMPDIR" or "HOME" or "USERPROFILE" or "LOCALAPPDATA" or "LANG"))
                    launch.Environment.Remove(key);
            launch.Environment["VISOLOOM_REFERENCE_TOKEN"] = credentials.Token;
            launch.Environment["VISOLOOM_REFERENCE_PARENT_PIPE"] = "1";
            launch.Environment["VISOLOOM_ENGINE_CONFIG"] = connection;
            launch.Environment["VISOLOOM_REFERENCE_STORAGE"] = Path.Combine(LocalDevelopmentHandshake.DevelopmentDataDirectory(), "rhino-session");

            var process = Process.Start(launch) ?? throw new ProviderException("engineUnavailable", "Could not start the local engine.");
            _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            bool ready = false;
            try
            {
                using var provider = new LocalProviderClient(credentials.Endpoint, credentials.Token);
                while (!process.HasExited)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        await provider.DiscoverAsync(ct).ConfigureAwait(false);
                        Volatile.Write(ref current, new Session(process, credentials));
                        ready = true;
                        return credentials;
                    }
                    catch (ProviderException ex) when (ex.Code is "engineUnavailable" or "authenticationRequired")
                    {
                        await Task.Delay(250, ct).ConfigureAwait(false);
                    }
                }
                throw new ProviderException("engineUnavailable", "The local engine stopped during startup. Close any other local service and try again.");
            }
            finally
            {
                if (!ready)
                {
                    try
                    {
                        await process.StandardInput.DisposeAsync().ConfigureAwait(false);
                        using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // An installation check can still be starting. It has no render jobs.
                        if (IsRunning(process)) process.Kill(entireProcessTree: true);
                    }
                    finally { process.Dispose(); }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            throw new ProviderException("engineUnavailable", "Could not start the local engine. Check its installation and try again.");
        }
        finally { Startup.Release(); }
    }

    private static bool IsRunning(Process process)
    {
        try { return !process.HasExited; }
        catch (InvalidOperationException) { return false; }
    }
}

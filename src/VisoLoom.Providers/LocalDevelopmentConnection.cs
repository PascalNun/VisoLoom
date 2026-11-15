using System.IO.Pipes;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

public static class LocalDevelopmentConnection
{
    public static async Task<LocalDevelopmentCredentials?> FindAsync(CancellationToken cancellationToken = default,
        string pipeName = LocalDevelopmentHandshake.PipeName, string? handoffDirectory = null)
    {
        var viaPipe = await TryPipeAsync(cancellationToken, pipeName).ConfigureAwait(false);
        return viaPipe ?? TryReadHandoffFile(handoffDirectory);
    }

    private static async Task<LocalDevelopmentCredentials?> TryPipeAsync(CancellationToken cancellationToken, string pipeName)
    {
        LocalDevelopmentHandshake.ValidatePipeName(pipeName);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            while (true)
            {
                // "." means this computer. Both ends require the same OS user.
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (IOException ex)
                    when (OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 232)
                {
                    // ERROR_NO_DATA: the previous client closed before the server recycled its
                    // instance. Retry only this local connection race, within the original deadline.
                    await Task.Delay(20, deadline.Token).ConfigureAwait(false);
                    continue;
                }
                byte[] payload = new byte[LocalDevelopmentHandshake.PayloadLength];
                try
                {
                    await pipe.ReadExactlyAsync(payload, deadline.Token).ConfigureAwait(false);
                    var connection = LocalDevelopmentHandshake.Decode(payload);
                    EndpointPolicy.RequireLocalEndpoint(connection.Endpoint);
                    return connection;
                }
                finally
                {
                    Array.Clear(payload);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static LocalDevelopmentCredentials? TryReadHandoffFile(string? directory)
    {
        // The pipe stays the primary handoff. This file is the same private payload
        // for a Wine-hosted plug-in, whose named-pipe client cannot reach a native
        // development service across the Wine boundary. The connection it yields
        // is still validated by a full capabilities exchange before anything uses it.
        byte[] payload = new byte[LocalDevelopmentHandshake.PayloadLength + 1];
        try
        {
            string path = Path.Combine(directory ?? LocalDevelopmentHandshake.DevelopmentDataDirectory(),
                LocalDevelopmentHandshake.HandoffFileName);
            if (!File.Exists(path))
                return null;
            if (!OperatingSystem.IsWindows() && File.ResolveLinkTarget(path, returnFinalTarget: false) is not null)
                return null;
            int total = 0;
            using (var stream = File.Open(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Options = FileOptions.SequentialScan }))
            {
                int count;
                while (total < payload.Length && (count = stream.Read(payload, total, payload.Length - total)) != 0)
                    total += count;
            }
            if (total != LocalDevelopmentHandshake.PayloadLength)
                return null;
            // The read buffer carries one guard byte; decode the exact payload only.
            byte[] exact = payload[..LocalDevelopmentHandshake.PayloadLength];
            try
            {
                var connection = LocalDevelopmentHandshake.Decode(exact);
                EndpointPolicy.RequireLocalEndpoint(connection.Endpoint);
                return connection;
            }
            finally
            {
                Array.Clear(exact);
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
        finally
        {
            Array.Clear(payload);
        }
    }
}

using System.IO.Pipes;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

public static class LocalDevelopmentConnection
{
    public static async Task<LocalDevelopmentCredentials?> FindAsync(CancellationToken cancellationToken = default, string pipeName = LocalDevelopmentHandshake.PipeName)
    {
        LocalDevelopmentHandshake.ValidatePipeName(pipeName);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            // "." means this computer. Both ends require the same OS user.
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(300, deadline.Token).ConfigureAwait(false);
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

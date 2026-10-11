using System.IO.Pipes;
using VisoLoom.Protocol;

namespace VisoLoom.ReferenceServer;

// Enabled explicitly by the test launcher, never by ordinary HTTP requests.
internal sealed class LocalDevelopmentConnectionService(ReferenceServerOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        byte[] payload = LocalDevelopmentHandshake.Encode(options.Port, options.BearerToken);
        try
        {
            using var pipe = new NamedPipeServerStream(options.DevelopmentPipeName!, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(stoppingToken);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(1));
                    await pipe.WriteAsync(payload, deadline.Token);
                    await pipe.FlushAsync(deadline.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // A stalled client must not prevent the next local connection.
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Cross-user connections and interrupted clients receive no fallback channel.
                }
                finally
                {
                    if (pipe.IsConnected)
                        pipe.Disconnect();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            Array.Clear(payload);
        }
    }
}

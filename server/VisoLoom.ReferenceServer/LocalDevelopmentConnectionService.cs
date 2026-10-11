using System.IO.Pipes;
using VisoLoom.Protocol;

namespace VisoLoom.ReferenceServer;

// Enabled explicitly by the test launcher, never by ordinary HTTP requests.
internal sealed class LocalDevelopmentConnectionService(ReferenceServerOptions options) : BackgroundService
{
    private NamedPipeServerStream? listener;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // .NET 10 schedules ExecuteAsync in the background. Publish the private listener before
        // host startup reports readiness, including immediately after a token-rotating restart.
        listener = new NamedPipeServerStream(options.DevelopmentPipeName!, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        byte[] payload = LocalDevelopmentHandshake.Encode(options.Port, options.BearerToken);
        try
        {
            using var pipe = listener ?? throw new InvalidOperationException("The development listener has not started.");
            while (!stoppingToken.IsCancellationRequested)
            {
                bool connected = false;
                try
                {
                    await pipe.WaitForConnectionAsync(stoppingToken);
                    connected = true;
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(1));
                    await pipe.WriteAsync(payload, deadline.Token);
                    await pipe.FlushAsync(deadline.Token);
                    // Windows discards unread buffered bytes on Disconnect. Wait for the client to
                    // finish reading and close, within the same deadline, before recycling the pipe.
                    await pipe.ReadAsync(new byte[1], deadline.Token);
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
                    if (connected)
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

    public override void Dispose()
    {
        base.Dispose();
        listener?.Dispose();
    }
}

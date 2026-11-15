using System.IO.Pipes;
using VisoLoom.Protocol;

namespace VisoLoom.ReferenceServer;

// Enabled explicitly by the test launcher, never by ordinary HTTP requests.
internal sealed class LocalDevelopmentConnectionService(ReferenceServerOptions options) : BackgroundService
{
    private NamedPipeServerStream? listener;
    private byte[] payload = [];
    private string? handoffPath;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // .NET 10 schedules ExecuteAsync in the background. Publish the private listener before
        // host startup reports readiness, including immediately after a token-rotating restart.
        listener = new NamedPipeServerStream(options.DevelopmentPipeName!, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        // The same payload as a file: a Wine-hosted plug-in cannot reach the named
        // pipe across the Wine boundary, but it reads the native private file.
        payload = LocalDevelopmentHandshake.Encode(options.Port, options.BearerToken);
        handoffPath = options.DevelopmentHandoffPath ?? LocalDevelopmentHandshake.HandoffFilePath();
        PublishHandoffFile();
        return base.StartAsync(cancellationToken);
    }

    private void PublishHandoffFile()
    {
        PrivateStorage.CreateDirectory(Path.GetDirectoryName(handoffPath!)!);
        string target = handoffPath!;
        string temporary = handoffPath + ".new";
        try
        {
            using (var stream = File.Open(temporary, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Options = FileOptions.WriteThrough }))
            {
                stream.Write(payload);
                stream.Flush();
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, target, overwrite: true);
        }
        catch (IOException)
        {
            // A later launcher overwrites a stale file with the next live token.
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
        }
    }

    private void RemoveHandoffFile()
    {
        try
        {
            if (handoffPath is not null && File.Exists(handoffPath))
                File.Delete(handoffPath);
        }
        catch (IOException)
        {
            // A stale file only carries a token that no future server accepts.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        RemoveHandoffFile();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                    int count = await pipe.ReadAsync(new byte[1], deadline.Token);
                    if (count != 0)
                        throw new IOException("Unexpected development handoff input.");
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
    }

    public override void Dispose()
    {
        base.Dispose();
        listener?.Dispose();
        Array.Clear(payload);
    }
}

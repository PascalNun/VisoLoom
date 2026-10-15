using System.IO.Pipes;
using VisoLoom.Protocol;
using VisoLoom.Providers;
using Xunit;

namespace VisoLoom.Protocol.Tests;

// These tests measure a real one-second discovery deadline. Concurrent server
// startup/JIT work can starve their timer continuations on Windows CI runners.
[CollectionDefinition("Local service discovery", DisableParallelization = true)]
public sealed class LocalServiceDiscoveryCollection { }

[Collection("Local service discovery")]
public sealed class LocalDevelopmentConnectionTests
{
    [Fact]
    public void HandshakeOnlyAllowsLiteralLoopbackAndDoesNotPrintCredentials()
    {
        string token = new('A', 64);
        var connection = LocalDevelopmentHandshake.Decode(LocalDevelopmentHandshake.Encode(5179, token));
        Assert.Equal(new Uri("http://127.0.0.1:5179/"), connection.Endpoint);
        Assert.DoesNotContain(token, connection.ToString());
        Assert.Throws<ArgumentException>(() => LocalDevelopmentHandshake.Encode(0, token));
        Assert.Throws<ArgumentException>(() => LocalDevelopmentHandshake.Encode(65536, token));
        Assert.Throws<ArgumentException>(() => LocalDevelopmentHandshake.Encode(5179, new string('x', 64)));
        Assert.Throws<ArgumentException>(() => LocalDevelopmentHandshake.Decode(new byte[65]));
        Assert.Throws<ArgumentException>(() => LocalDevelopmentHandshake.Decode(new byte[66]));
        Assert.Throws<ArgumentException>(() => LocalDevelopmentHandshake.ValidatePipeName("../another-user"));
    }

    [Fact]
    public async Task MissingServiceReturnsNoConnectionAndCallerCancellationPropagates()
    {
        string pipeName = NewPipeName();
        Assert.Null(await LocalDevelopmentConnection.FindAsync(pipeName: pipeName));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LocalDevelopmentConnection.FindAsync(cancelled.Token, pipeName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteOrMalformedHandoffIsRejected(bool malformed)
    {
        string pipeName = NewPipeName();
        using var pipe = CreatePipe(pipeName);
        var waiting = pipe.WaitForConnectionAsync();
        var finding = LocalDevelopmentConnection.FindAsync(pipeName: pipeName);
        await waiting;
        await pipe.WriteAsync(new byte[malformed ? 66 : 10]);
        pipe.Disconnect();
        Assert.Null(await finding);
    }

    [Fact]
    public async Task ClosingListenerCanFinishRecyclingWithinTheDiscoveryDeadline()
    {
        string pipeName = NewPipeName();
        string token = new('B', 64);
        byte[] payload = LocalDevelopmentHandshake.Encode(5179, token);
        using var pipe = CreatePipe(pipeName);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var waiting = pipe.WaitForConnectionAsync(deadline.Token);
        using (var previous = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            await previous.ConnectAsync(deadline.Token);
            await waiting;
        }
        try
        {
            Assert.Equal(0, await pipe.ReadAsync(new byte[1], deadline.Token));
        }
        catch (IOException ex) when (OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 109)
        {
            // A client that closed before the first read may surface ERROR_BROKEN_PIPE
            // instead of an EOF read on Windows. Both mean the old client has closed.
        }
        var finding = LocalDevelopmentConnection.FindAsync(pipeName: pipeName);
        // Model a busy host finishing the previous client's close. The old 300 ms connect
        // limit could expire even though the one-second discovery deadline had time left.
        await Task.Delay(400, deadline.Token);
        pipe.Disconnect();
        await pipe.WaitForConnectionAsync(deadline.Token);
        await pipe.WriteAsync(payload, deadline.Token);
        var connection = await finding;
        Assert.NotNull(connection);
        Assert.Equal(token, connection.Token);
        Assert.Equal(new Uri("http://127.0.0.1:5179/"), connection.Endpoint);
    }

    [Fact]
    public async Task StalledHandoffTimesOutWithoutBlockingTheCaller()
    {
        string pipeName = NewPipeName();
        using var pipe = CreatePipe(pipeName);
        var waiting = pipe.WaitForConnectionAsync();
        var finding = LocalDevelopmentConnection.FindAsync(pipeName: pipeName);
        await waiting;
        Assert.Null(await finding);
    }

    private static string NewPipeName() => "vl-" + Guid.NewGuid().ToString("N")[..16];
    private static NamedPipeServerStream CreatePipe(string name) => new(name, PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
}

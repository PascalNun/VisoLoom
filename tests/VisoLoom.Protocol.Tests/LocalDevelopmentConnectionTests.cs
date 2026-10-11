using System.IO.Pipes;
using VisoLoom.Protocol;
using VisoLoom.Providers;
using Xunit;

namespace VisoLoom.Protocol.Tests;

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

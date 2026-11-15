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
public sealed class LocalDevelopmentConnectionTests : IDisposable
{
    private readonly string emptyHandoff;

    public LocalDevelopmentConnectionTests()
    {
        // Every discovery test pins its own handoff directory so the machine's
        // live development session cannot leak into the results, and so the
        // file-based handoff can be exercised explicitly.
        emptyHandoff = Path.Combine(Path.GetTempPath(), "VisoLoom-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyHandoff);
    }

    public void Dispose() => Directory.Delete(emptyHandoff, recursive: true);

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
        Assert.Null(await LocalDevelopmentConnection.FindAsync(pipeName: pipeName, handoffDirectory: emptyHandoff));
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
        var finding = LocalDevelopmentConnection.FindAsync(pipeName: pipeName, handoffDirectory: emptyHandoff);
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
        var finding = LocalDevelopmentConnection.FindAsync(pipeName: pipeName, handoffDirectory: emptyHandoff);
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
        var finding = LocalDevelopmentConnection.FindAsync(pipeName: pipeName, handoffDirectory: emptyHandoff);
        await waiting;
        Assert.Null(await finding);
    }

    [Fact]
    public async Task HandoffFileConnectsWhenNoPipeIsListening()
    {
        string token = new('C', 64);
        File.WriteAllBytes(Path.Combine(emptyHandoff, LocalDevelopmentHandshake.HandoffFileName), LocalDevelopmentHandshake.Encode(5180, token));
        var connection = await LocalDevelopmentConnection.FindAsync(pipeName: NewPipeName(), handoffDirectory: emptyHandoff);
        Assert.NotNull(connection);
        Assert.Equal(new Uri("http://127.0.0.1:5180/"), connection!.Endpoint);
        Assert.Equal(token, connection.Token);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(67)]
    public async Task TruncatedOrOverlongHandoffFileIsRejected(int length)
    {
        byte[] valid = LocalDevelopmentHandshake.Encode(5179, new string('C', 64));
        byte[] payload = length switch
        {
            0 => [],
            67 => [.. valid, 0],
            _ => valid[..length]
        };
        File.WriteAllBytes(Path.Combine(emptyHandoff, LocalDevelopmentHandshake.HandoffFileName), payload);
        Assert.Null(await LocalDevelopmentConnection.FindAsync(pipeName: NewPipeName(), handoffDirectory: emptyHandoff));
    }

    [Fact]
    public async Task HandoffFileWithForeignTokenIsRejected()
    {
        byte[] valid = LocalDevelopmentHandshake.Encode(5179, new string('C', 64));
        valid[2] = (byte)'z';
        File.WriteAllBytes(Path.Combine(emptyHandoff, LocalDevelopmentHandshake.HandoffFileName), valid);
        Assert.Null(await LocalDevelopmentConnection.FindAsync(pipeName: NewPipeName(), handoffDirectory: emptyHandoff));
    }

    [Fact]
    public async Task LinkedHandoffFileIsRejectedFailClosed()
    {
        if (OperatingSystem.IsWindows())
            return; // The Wine path resolves through the mounted drive, not host links.
        string token = new('D', 64);
        string real = Path.Combine(emptyHandoff, "real.dat");
        File.WriteAllBytes(real, LocalDevelopmentHandshake.Encode(5179, token));
        string link = Path.Combine(emptyHandoff, LocalDevelopmentHandshake.HandoffFileName);
        File.CreateSymbolicLink(link, real);
        Assert.Null(await LocalDevelopmentConnection.FindAsync(pipeName: NewPipeName(), handoffDirectory: emptyHandoff));
    }

    [Fact]
    public void DevelopmentDataDirectoryIsAPrivatePerUserLocation()
    {
        var path = LocalDevelopmentHandshake.DevelopmentDataDirectory();
        Assert.True(Path.IsPathFullyQualified(path));
        Assert.Equal("development-server", Path.GetFileName(path));
        Assert.EndsWith("VisoLoom" + Path.DirectorySeparatorChar + "development-server", path);
    }

    private static string NewPipeName() => "vl-" + Guid.NewGuid().ToString("N")[..16];
    private static NamedPipeServerStream CreatePipe(string name) => new(name, PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
}

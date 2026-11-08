using VisoLoom.Core;
using VisoLoom.Protocol;
using VisoLoom.Providers;
using VisoLoom.Tests;
using Xunit;

namespace VisoLoom.Protocol.Tests;

public sealed class BflImageProviderTests
{
    [Fact]
    public void CloudOptInIsRequiredBeforeAnyNetworkOperation()
    {
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        Assert.Throws<ArgumentException>(() => new BflImageProvider("unit-test-key", false, handler));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task DiscoveryReturnsThePinnedCapabilitiesWithoutARequestOrCredentialUse()
    {
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        using var provider = new BflImageProvider("unit-test-key", true, handler);
        var capabilities = await provider.DiscoverAsync();
        Assert.Equal("bfl", capabilities.ProviderId);
        Assert.Equal(BflImageProvider.ModelId, capabilities.Models.Single().Id);
        Assert.False(capabilities.Models.Single().SupportsCancellation);
        Assert.Null(capabilities.MaximumDecodedPixels);
        Assert.Null(capabilities.MaximumImageBytes);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task RenderFailsClosedUntilSizeResearchPinsTheEndpointLimits()
    {
        var (scene, source, plan, intent) = Fixture(null);
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        using var provider = new BflImageProvider("unit-test-key", true, handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("sizeContractUnpinned", error.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task PinnedSizesStillFailClosedOnTheUnresolvedReferenceUploadContract()
    {
        var (scene, source, plan, intent) = Fixture(Pinned());
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        using var provider = new BflImageProvider("unit-test-key", true, handler, Pinned());
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("referenceUploadUnresolved", error.Code);
        Assert.Contains("No image was sent", error.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task InvalidScenesAreRejectedBeforeAnyGateOrNetwork()
    {
        var (scene, source, plan, intent) = Fixture(null);
        scene = scene with { SceneId = Guid.Empty };
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        using var provider = new BflImageProvider("unit-test-key", true, handler);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal(0, handler.Calls);
    }

    private static ResolutionConstraints Pinned() => new()
    {
        IsKnown = true,
        MinEdge = 64,
        MaxEdge = 2048,
        MinPixels = 64 * 64,
        MaxPixels = 4_194_304,
        DimensionMultiple = 1,
        AllowUpscaling = false
    };

    private static RepresentationPreset Preset() => new() { Id = "test", Name = "Test", BaseInstruction = "Refine visible materials." };

    // Simulates the endpoint size table the pending research must pin; the default keeps the adapter fail-closed.
    private static (SceneManifest Scene, byte[] Png, ResolutionPlan Plan, RenderIntent Intent) Fixture(ResolutionConstraints? pinned)
    {
        var scene = SceneFixture.Create();
        byte[] png = SceneFixture.Png;
        var plan = pinned is null ? SceneFixture.Plan(scene.Frame) : ResolutionPlanner.Plan(scene.Frame, scene.Frame, pinned);
        return (scene, png, plan, ScenePromptComposer.Compose(scene, Preset(), "Reviewed intent", plan, false));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(handle(request));
        }
    }
}

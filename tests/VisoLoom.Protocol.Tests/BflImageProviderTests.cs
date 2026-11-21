using System.Net;
using System.Text;
using System.Text.Json;
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
    public async Task DiscoveryChecksTheKeyAgainstTheDocumentedCreditsEndpoint()
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.bfl.ai/v1/credits", request.RequestUri!.AbsoluteUri);
            Assert.Equal("unit-test-key", request.Headers.GetValues("x-key").Single());
            Assert.Null(request.Content);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"credits\":123}", Encoding.UTF8, "application/json") };
        });
        using var provider = new BflImageProvider("unit-test-key", true, handler);
        var capabilities = await provider.DiscoverAsync();
        Assert.Equal("bfl", capabilities.ProviderId);
        // No model list exists; the connection check validates the key, and the
        // documented curated pair — the pinned pro model and its preview — is offered.
        Assert.Equal(new[] { BflImageProvider.ModelId, BflImageProvider.PreviewModelId },
            capabilities.Models.Select(model => model.Id).ToArray());
        Assert.All(capabilities.Models, model =>
        {
            Assert.Equal(model.Id, model.Version);
            Assert.Equal(BflImageProvider.DocumentedResolution, model.Resolution);
            Assert.False(model.SupportsCancellation);
        });
        Assert.Equal(BflImageProvider.DocumentedResolution.MaxPixels, capabilities.MaximumDecodedPixels);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"balance\":123}")]
    [InlineData("{\"credits\":\"nan\"}")]
    public async Task InvalidCreditsResponsesFailClosed(string json)
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        using var provider = new BflImageProvider("unit-test-key", true, handler);
        var failure = await Assert.ThrowsAsync<ProviderException>(() => provider.DiscoverAsync());
        Assert.Equal("invalidResponse", failure.Code);
        Assert.DoesNotContain(json, failure.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ARejectedKeyFailsClosedWithoutExposingTheProviderBody()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("private-key-detail", Encoding.UTF8, "application/json")
        });
        using var provider = new BflImageProvider("unit-test-key", true, handler);
        var failure = await Assert.ThrowsAsync<ProviderException>(() => provider.DiscoverAsync());
        Assert.Equal("bflRejected", failure.Code);
        Assert.DoesNotContain("private-key-detail", failure.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("flux-2-klein-9b")]
    [InlineData("flux-2-pro; drop table")]
    public void OnlyTheDocumentedProModelsAreSelectable(string modelId)
    {
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        Assert.Throws<ArgumentException>(() => new BflImageProvider("unit-test-key", true, handler, modelId));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task RenderingFailsClosedOnTheUnresolvedReferenceUploadForEitherDocumentedModel()
    {
        foreach (var modelId in new[] { BflImageProvider.ModelId, BflImageProvider.PreviewModelId })
        {
            var (scene, source, plan, intent) = Fixture(modelId);
            using var handler = new Handler(_ => throw new Exception("Network must not run."));
            using var provider = new BflImageProvider("unit-test-key", true, handler, modelId);
            var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
                new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
            Assert.Equal("referenceUploadUnresolved", error.Code);
            Assert.Contains("No image was sent", error.Message);
            Assert.Contains("no reference host was contacted", error.Message);
            Assert.Equal(0, handler.Calls);
        }
    }

    [Fact]
    public async Task InvalidScenesAreRejectedBeforeAnyGateOrNetwork()
    {
        var scene = SceneFixture.Create();
        var empty = scene with { Passes = [] };
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        using var provider = new BflImageProvider("unit-test-key", true, handler);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.RenderAsync(empty,
            new Dictionary<string, byte[]> { ["reference.png"] = SceneFixture.Png }, new RenderIntent { RequestedOutput = scene.Frame },
            ResolutionPlanner.Plan(scene.Frame, scene.Frame, BflImageProvider.DocumentedResolution)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void TheDocumentedContractPlansACompleteFrameWithinTheDocumentedLimits()
    {
        var size = new PixelSize(1222, 893);
        var plan = ResolutionPlanner.Plan(size, size, BflImageProvider.AccountModel(BflImageProvider.ModelId).Resolution);
        Assert.Equal(size, plan.Final);
        // The 16-pixel alignment is VisoLoom's own planning choice; the limits
        // (at least 64 per side, up to 4 MP) are the documented pro contract.
        Assert.Equal(0, plan.Native.Width % 16);
        Assert.Equal(0, plan.Native.Height % 16);
        Assert.True(plan.Native.Width >= 64 && plan.Native.Height >= 64);
        Assert.InRange(plan.Native.Pixels, 4_096, 4_194_304);
    }

    private static RepresentationPreset Preset() => new() { Id = "test", Name = "Test", BaseInstruction = "Refine visible materials." };

    private static (SceneManifest Scene, byte[] Png, ResolutionPlan Plan, RenderIntent Intent) Fixture(string modelId = BflImageProvider.ModelId)
    {
        var scene = SceneFixture.Create();
        byte[] png = SceneFixture.Png;
        var plan = ResolutionPlanner.Plan(scene.Frame, scene.Frame, BflImageProvider.AccountModel(modelId).Resolution);
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

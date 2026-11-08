using System.Net;
using System.Text;
using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;
using VisoLoom.Providers;
using VisoLoom.Tests;
using Xunit;

namespace VisoLoom.Protocol.Tests;

public sealed class XaiImageProviderTests
{
    [Fact]
    public void CloudOptInIsRequiredBeforeAnyNetworkOperation()
    {
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        Assert.Throws<ArgumentException>(() => new XaiImageProvider("unit-test-key", false, new Codec(), handler));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task DiscoveryChecksThePinnedModelWithoutSendingAnImage()
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.x.ai/v1/models", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("unit-test-key", request.Headers.Authorization!.Parameter);
            Assert.Null(request.Content);
            return Json(new { data = new[] { new { id = "grok-4" }, new { id = XaiImageProvider.ModelId } } });
        });
        using var provider = new XaiImageProvider("unit-test-key", true, new Codec(), handler);
        var capabilities = await provider.DiscoverAsync();
        Assert.Equal("xai", capabilities.ProviderId);
        Assert.Equal(XaiImageProvider.ModelId, capabilities.Models.Single().Id);
        Assert.False(capabilities.Models.Single().SupportsCancellation);
        Assert.Null(capabilities.MaximumDecodedPixels);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RenderFailsClosedUntilSizeResearchPinsTheTierTable()
    {
        var (scene, source, plan, intent) = Fixture(null);
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        using var provider = new XaiImageProvider("unit-test-key", true, new Codec(), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("sizeContractUnpinned", error.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task EditSendsOneReferenceAndTheExactRecordedPromptAndReturnsValidatedProvenance()
    {
        var (scene, source, plan, intent) = Fixture(Pinned());
        using var handler = new Handler(request =>
        {
            Assert.Equal("https://api.x.ai/v1/images/edits", request.RequestUri!.AbsoluteUri);
            using var json = JsonDocument.Parse(request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            var body = json.RootElement;
            Assert.Equal(XaiImageProvider.ModelId, body.GetProperty("model").GetString());
            Assert.Equal(intent.EffectivePrompt, body.GetProperty("prompt").GetString());
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(source), body.GetProperty("image").GetProperty("url").GetString());
            Assert.Equal("image_url", body.GetProperty("image").GetProperty("type").GetString());
            Assert.False(body.TryGetProperty("image_urls", out _));
            Assert.False(body.TryGetProperty("scene", out _));
            return Image(source);
        });
        using var provider = new XaiImageProvider("unit-test-key", true, new Codec(), handler, Pinned());
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan);
        Assert.Equal(source, result.PngBytes);
        Assert.False(result.Metadata.IsUnchangedReference);
        Assert.Equal("xai", result.Metadata.ProviderId);
        Assert.Equal(scene.SceneId, result.Metadata.SceneId);
        Assert.Equal(intent.DisclosedContextFields, result.Metadata.DisclosedContextFields);
        Assert.Equal(ContentDigest.Sha256(source), result.Metadata.OutputSha256);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"data\":[]}")]
    [InlineData("{\"data\":[{\"id\":\"a-different-model\"}]}")]
    public async Task InvalidDiscoveryFailsSafelyWithoutChoosingAnotherModel(string json)
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        using var provider = new XaiImageProvider("unit-test-key", true, new Codec(), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.DiscoverAsync());
        Assert.DoesNotContain(json, error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"data\":[null]}")]
    [InlineData("{\"data\":[{\"b64_json\":\"not-base64\"}]}")]
    public async Task InvalidResultNeverRetries(string json)
    {
        var (scene, source, plan, intent) = Fixture(Pinned());
        using var handler = new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        using var provider = new XaiImageProvider("unit-test-key", true, new Codec(), handler, Pinned());
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(
            scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("invalidResult", error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{\"data\":[{\"url\":\"https://example.invalid/image\"}]}")]
    [InlineData("{\"url\":\"https://example.invalid/image\"}")]
    public async Task HostedUrlResultsFailClosedUntilTheDownloadContractIsResearched(string json)
    {
        var (scene, source, plan, intent) = Fixture(Pinned());
        using var handler = new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        using var provider = new XaiImageProvider("unit-test-key", true, new Codec(), handler, Pinned());
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(
            scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("urlResultNotSupported", error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task RejectionDoesNotRetryOrExposeProviderBody(int status)
    {
        var (scene, source, plan, intent) = Fixture(Pinned());
        using var handler = new Handler(_ => new((HttpStatusCode)status)
        {
            Content = new StringContent("private-prompt-and-key"),
            Headers = { Location = new Uri("https://example.invalid/steal") }
        });
        using var provider = new XaiImageProvider("unit-test-key", true, new Codec(), handler, Pinned());
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.DoesNotContain("private-prompt-and-key", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AmbiguousSubmissionNeverRetriesOrIncludesSecretInError()
    {
        var (scene, source, plan, intent) = Fixture(Pinned());
        using var handler = new Handler(_ => throw new HttpRequestException("sensitive-server-detail"));
        using var provider = new XaiImageProvider("unit-test-key", true, new Codec(), handler, Pinned());
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("generationUnconfirmed", error.Code);
        Assert.DoesNotContain("sensitive-server-detail", error.Message);
        Assert.DoesNotContain("unit-test-key", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    private static ResolutionConstraints Pinned() => new()
    {
        IsKnown = true,
        MinEdge = 1,
        MaxEdge = 4096,
        MinPixels = 1,
        MaxPixels = 8_294_400,
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

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Image(byte[] png) => Json(new { data = new[] { new { b64_json = Convert.ToBase64String(png) } } });
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(handle(request));
        }
    }
    private sealed class Codec : IImageProcessor
    {
        public byte[] PrepareReference(byte[] png, ResolutionPlan plan) => png;
        public byte[] NormalizeResult(byte[] png, ResolutionPlan plan) => png;
    }
}

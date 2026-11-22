using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;
using VisoLoom.Providers;
using VisoLoom.Tests;
using Xunit;

namespace VisoLoom.Protocol.Tests;

public sealed class GoogleImageProviderTests
{
    [Fact]
    public void CloudOptInIsRequiredBeforeAnyNetworkOperation()
    {
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        Assert.Throws<ArgumentException>(() => new GoogleImageProvider("unit-test-key", false, new Codec(), handler));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task DiscoveryKeepsTheAccountsImageModelsInListingOrder()
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000", request.RequestUri!.AbsoluteUri);
            Assert.Equal("unit-test-key", request.Headers.GetValues("x-goog-api-key").Single());
            Assert.Null(request.Content);
            return Json(new
            {
                models = new object[]
                {
                    new { name = "models/gemini-3.1-flash-lite-image" },
                    new { name = "models/gemini-3.0-pro" },
                    new { name = "models/gemini-nano-banana-2.1" },
                    new { name = "models/gemini-3.1-flash-image" }
                }
            });
        });
        using var provider = new GoogleImageProvider("unit-test-key", true, new Codec(), handler);
        var capabilities = await provider.DiscoverAsync();
        Assert.Equal("google", capabilities.ProviderId);
        // The listing carries no creation dates; text models are filtered and the provider's order is kept.
        Assert.Equal(new[] { "gemini-3.1-flash-lite-image", "gemini-nano-banana-2.1", "gemini-3.1-flash-image" },
            capabilities.Models.Select(model => model.Id).ToArray());
        Assert.All(capabilities.Models, model => Assert.Equal(model.Id, model.Version));
        Assert.True(capabilities.Models.All(model => !model.SupportsCancellation));
        Assert.NotNull(capabilities.MaximumDecodedPixels);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DiscoveryRejectsAnAccountWithoutAnyImageModel()
    {
        using var handler = new Handler(_ => Json(new { models = new object[] { new { name = "models/gemini-3.0-pro" } } }));
        using var provider = new GoogleImageProvider("unit-test-key", true, new Codec(), handler);
        var failure = await Assert.ThrowsAsync<ProviderException>(() => provider.DiscoverAsync());
        Assert.Equal("modelUnavailable", failure.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DiscoveryRejectsAMalformedOrOversizedModelList()
    {
        using var oversized = new Handler(_ => Json(new { models = Enumerable.Range(0, 1025).Select(i => new { name = "models/gemini-image-" + i }) }));
        using var provider = new GoogleImageProvider("unit-test-key", true, new Codec(), oversized);
        await Assert.ThrowsAsync<ProviderException>(() => provider.DiscoverAsync());

        using var malformed = new Handler(_ => Json(new { listed = Array.Empty<object>() }));
        using var rejected = new GoogleImageProvider("unit-test-key", true, new Codec(), malformed);
        var failure = await Assert.ThrowsAsync<ProviderException>(() => rejected.DiscoverAsync());
        Assert.Equal("invalidResponse", failure.Code);
    }

    [Fact]
    public async Task EditSendsOneReferenceAndTheExactRecordedPromptAndReturnsValidatedProvenance()
    {
        var (scene, source, plan, intent) = Fixture();
        byte[] prepared = Png(plan.Native);
        using var handler = new Handler(request =>
        {
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/interactions", request.RequestUri!.AbsoluteUri);
            using var json = JsonDocument.Parse(request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            var body = json.RootElement;
            Assert.Equal(GoogleImageProvider.ModelId, body.GetProperty("model").GetString());
            Assert.Equal(2, body.GetProperty("input").GetArrayLength());
            Assert.Equal("text", body.GetProperty("input")[0].GetProperty("type").GetString());
            Assert.Equal(intent.EffectivePrompt, body.GetProperty("input")[0].GetProperty("text").GetString());
            Assert.Equal("image", body.GetProperty("input")[1].GetProperty("type").GetString());
            Assert.Equal("image/png", body.GetProperty("input")[1].GetProperty("mime_type").GetString());
            Assert.Equal(Convert.ToBase64String(prepared), body.GetProperty("input")[1].GetProperty("data").GetString());
            Assert.Equal("image", body.GetProperty("response_format").GetProperty("type").GetString());
            Assert.Equal("image/png", body.GetProperty("response_format").GetProperty("mime_type").GetString());
            // The request names the ratio the canvas is planned at; the tier stays at its
            // documented default because the image_size strings conflict between the pages.
            Assert.Equal(GoogleImageProvider.AspectRatioFor(plan.Native), body.GetProperty("response_format").GetProperty("aspect_ratio").GetString());
            Assert.False(body.GetProperty("response_format").TryGetProperty("image_size", out _));
            Assert.False(body.TryGetProperty("tools", out _));
            return Image(source);
        });
        using var provider = new GoogleImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan);
        Assert.Equal(source, result.PngBytes);
        Assert.False(result.Metadata.IsUnchangedReference);
        Assert.Equal("google", result.Metadata.ProviderId);
        Assert.Equal(GoogleImageProvider.ModelId, result.Metadata.ModelId);
        Assert.Equal(GoogleImageProvider.ModelId, result.Metadata.ModelVersion);
        Assert.Equal(scene.SceneId, result.Metadata.SceneId);
        Assert.Equal(intent.DisclosedContextFields, result.Metadata.DisclosedContextFields);
        Assert.Equal(ContentDigest.Sha256(source), result.Metadata.OutputSha256);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AnAccountListedImageModelEditsWithItsOwnIdInTheRequestAndProvenance()
    {
        const string listed = "gemini-3.1-flash-image";
        var (scene, source, plan, intent) = Fixture(listed);
        using var handler = new Handler(request =>
        {
            using var json = JsonDocument.Parse(request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            Assert.Equal(listed, json.RootElement.GetProperty("model").GetString());
            return Image(Png(plan.Native));
        });
        using var provider = new GoogleImageProvider("unit-test-key", true, new CanvasCodec(source), handler, modelId: listed);
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan);
        Assert.Equal(listed, result.Metadata.ModelId);
        Assert.Equal(listed, result.Metadata.ModelVersion);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void TheDocumentedContractLocksTheCanvasToANamedRatio()
    {
        var size = new PixelSize(1222, 893);
        var plan = ResolutionPlanner.Plan(size, size, GoogleImageProvider.AccountModel(GoogleImageProvider.ModelId).Resolution);
        Assert.Equal(size, plan.Final);
        Assert.Equal(0, plan.Native.Width % 16);
        Assert.Equal(0, plan.Native.Height % 16);
        // The canvas sits at the documented ratio that carries the frame at full scale
        // with the least padding (here 1344x896, exact 3:2) —
        // and the frame keeps its own proportions inside it, never a reframing.
        Assert.Equal("3:2", GoogleImageProvider.AspectRatioFor(plan.Native));
        Assert.Contains(GoogleImageProvider.DocumentedRatios, entry =>
            (long)plan.Native.Width * entry.Ratio.Height == (long)plan.Native.Height * entry.Ratio.Width);
        Assert.InRange(plan.Sampling!.Value.Width / plan.Sampling.Value.Height, size.Width / (double)size.Height - 0.001, size.Width / (double)size.Height + 0.001);
    }

    [Fact]
    public async Task ARejectionSurfacesTheStructuredMachineStatus()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":{\"code\":400,\"status\":\"INVALID_ARGUMENT\",\"message\":\"reflects private prompt text\"}}", Encoding.UTF8, "application/json")
        });
        using var provider = new GoogleImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Contains("HTTP 400 · INVALID_ARGUMENT", error.Message);
        // The structured status is the one diagnostic that reaches the message; free text stays private.
        Assert.DoesNotContain("reflects private prompt text", error.Message);
        // The bounded provider body rides on the exception for the machine-local log.
        Assert.Equal("{\"error\":{\"code\":400,\"status\":\"INVALID_ARGUMENT\",\"message\":\"reflects private prompt text\"}}", error.RejectionDiagnostics);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"name\":\"models/a-different-model\"}")]
    [InlineData("{\"name\":123}")]
    public async Task InvalidDiscoveryFailsSafelyWithoutChoosingAnotherModel(string json)
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        using var provider = new GoogleImageProvider("unit-test-key", true, new Codec(), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.DiscoverAsync());
        Assert.DoesNotContain(json, error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"output_image\":null}")]
    [InlineData("{\"output_image\":{\"mime_type\":\"image/jpeg\",\"data\":\"\"}}")]
    [InlineData("{\"output_image\":{\"data\":\"not-base64\"}}")]
    public async Task InvalidResultNeverFollowsUrlsOrRetries(string json)
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        using var provider = new GoogleImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(
            scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("invalidResult", error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task RejectionDoesNotRetryOrExposeProviderBody(int status)
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => new((HttpStatusCode)status)
        {
            Content = new StringContent("private-prompt-and-key"),
            Headers = { Location = new Uri("https://example.invalid/steal") }
        });
        using var provider = new GoogleImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.DoesNotContain("private-prompt-and-key", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AmbiguousSubmissionNeverRetriesOrIncludesSecretInError()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => throw new HttpRequestException("sensitive-server-detail"));
        using var provider = new GoogleImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("generationUnconfirmed", error.Code);
        Assert.DoesNotContain("sensitive-server-detail", error.Message);
        Assert.DoesNotContain("unit-test-key", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    private static RepresentationPreset Preset() => new() { Id = "test", Name = "Test", BaseInstruction = "Refine visible materials." };

    // The documented family contract plans the canvas; the listing's own id varies.
    private static (SceneManifest Scene, byte[] Png, ResolutionPlan Plan, RenderIntent Intent) Fixture(string modelId = GoogleImageProvider.ModelId)
    {
        var scene = SceneFixture.Create();
        byte[] png = SceneFixture.Png;
        var plan = ResolutionPlanner.Plan(scene.Frame, scene.Frame, GoogleImageProvider.AccountModel(modelId).Resolution);
        return (scene, png, plan, ScenePromptComposer.Compose(scene, Preset(), "Reviewed intent", plan, false));
    }

    // Authored opaque RGB PNG, compressed with the standard codec; no third-party image fixture.
    private static byte[] Png(PixelSize size)
    {
        using var encoded = new MemoryStream();
        encoded.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, size.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), size.Height);
        header[8] = 8;
        header[9] = 2;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            byte[] row = new byte[1 + size.Width * 3];
            Array.Fill(row, (byte)220, 1, row.Length - 1);
            for (int y = 0; y < size.Height; y++)
                zlib.Write(row);
        }
        Chunk("IDAT", compressed.ToArray());
        Chunk("IEND", []);
        return encoded.ToArray();
        void Chunk(string type, byte[] data)
        {
            byte[] number = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
            encoded.Write(number);
            byte[] name = Encoding.ASCII.GetBytes(type);
            encoded.Write(name);
            encoded.Write(data);
            uint crc = uint.MaxValue;
            foreach (byte value in name.Concat(data))
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
            }
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
            encoded.Write(number);
        }
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Image(byte[] png) => Json(new { output_image = new { mime_type = "image/png", data = Convert.ToBase64String(png) } });
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

    // Prepares the planned canvas the way the Rhino host does and restores the original frame.
    private sealed class CanvasCodec(byte[] final) : IImageProcessor
    {
        public byte[] PrepareReference(byte[] png, ResolutionPlan plan) => Png(plan.Native);
        public byte[] NormalizeResult(byte[] png, ResolutionPlan plan) => final;
    }
}

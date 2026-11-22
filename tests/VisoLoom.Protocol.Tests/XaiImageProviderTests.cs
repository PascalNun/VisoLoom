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
    public async Task DiscoveryKeepsTheAccountsImageModelsNewestFirst()
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.x.ai/v1/models", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("unit-test-key", request.Headers.Authorization?.Parameter);
            Assert.Null(request.Content);
            return Json(new
            {
                data = new object[]
                {
                    new { id = "grok-imagine-video-1.5", created = 1_779_000_000 },
                    new { id = "grok-4.7", created = 1_779_000_000 },
                    new { id = "grok-imagine-image-2.0-flash", created = 1_778_000_000 },
                    new { id = "grok-imagine-image-2.0", created = 1_775_000_000 },
                    new { id = "grok-imagine-image", created = 1_769_472_000 },
                    new { id = "grok-imagine-image-lite" }
                }
            });
        });
        using var provider = new XaiImageProvider("unit-test-key", true, new Codec(), handler);
        var capabilities = await provider.DiscoverAsync();
        Assert.Equal("xai", capabilities.ProviderId);
        // Text and video models are filtered; the documented created field
        // orders the rows newest first, and an entry without it sorts last.
        Assert.Equal(new[]
        {
            "grok-imagine-image-2.0-flash",
            "grok-imagine-image-2.0",
            "grok-imagine-image",
            "grok-imagine-image-lite"
        }, capabilities.Models.Select(model => model.Id).ToArray());
        Assert.All(capabilities.Models, model => Assert.Equal(model.Id, model.Version));
        Assert.All(capabilities.Models, model => Assert.False(model.SupportsCancellation));
        Assert.NotNull(capabilities.MaximumDecodedPixels);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DiscoveryRejectsAnAccountWithoutAnyImageModel()
    {
        using var handler = new Handler(_ => Json(new { data = new object[] { new { id = "grok-4.7", created = 1_769_472_000 } } }));
        using var provider = new XaiImageProvider("unit-test-key", true, new Codec(), handler);
        var failure = await Assert.ThrowsAsync<ProviderException>(() => provider.DiscoverAsync());
        Assert.Equal("modelUnavailable", failure.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DiscoveryRejectsAMalformedOrOversizedModelList()
    {
        using var oversized = new Handler(_ => Json(new { data = Enumerable.Range(0, 1025).Select(i => new { id = "grok-imagine-image-" + i, created = i }) }));
        using var provider = new XaiImageProvider("unit-test-key", true, new Codec(), oversized);
        await Assert.ThrowsAsync<ProviderException>(() => provider.DiscoverAsync());

        using var malformed = new Handler(_ => Json(new { listed = Array.Empty<object>() }));
        using var rejected = new XaiImageProvider("unit-test-key", true, new Codec(), malformed);
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
            Assert.Equal("https://api.x.ai/v1/images/edits", request.RequestUri!.AbsoluteUri);
            using var json = JsonDocument.Parse(request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            var body = json.RootElement;
            // Every field is the documented edit request.
            Assert.Equal(XaiImageProvider.ModelId, body.GetProperty("model").GetString());
            Assert.Equal(intent.EffectivePrompt, body.GetProperty("prompt").GetString());
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(prepared), body.GetProperty("image").GetProperty("url").GetString());
            Assert.Equal("image_url", body.GetProperty("image").GetProperty("type").GetString());
            Assert.Equal("b64_json", body.GetProperty("response_format").GetString());
            Assert.Equal("2k", body.GetProperty("resolution").GetString());
            // The request names the ratio the canvas is planned at.
            Assert.Equal(XaiImageProvider.AspectRatioFor(plan.Native), body.GetProperty("aspect_ratio").GetString());
            Assert.False(body.TryGetProperty("quality", out _));
            Assert.False(body.TryGetProperty("n", out _));
            return Image(prepared, "image/png");
        });
        using var provider = new XaiImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan);
        Assert.Equal(source, result.PngBytes);
        Assert.False(result.Metadata.IsUnchangedReference);
        Assert.Equal("xai", result.Metadata.ProviderId);
        Assert.Equal(XaiImageProvider.ModelId, result.Metadata.ModelId);
        Assert.Equal(XaiImageProvider.ModelId, result.Metadata.ModelVersion);
        Assert.Equal(scene.SceneId, result.Metadata.SceneId);
        Assert.Equal(intent.DisclosedContextFields, result.Metadata.DisclosedContextFields);
        Assert.Equal(ContentDigest.Sha256(source), result.Metadata.OutputSha256);
        Assert.Contains("2k tier requested", result.Metadata.ProcessingHistory[0]);
        // A PNG result adds no mime note to the record.
        Assert.DoesNotContain(result.Metadata.ProcessingHistory, line => line.StartsWith("Returned image mime", StringComparison.Ordinal));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ADeclaredJpegResultIsNormalizedWithItsMimeRecorded()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => Image(Png(plan.Native), "image/jpeg"));
        using var provider = new XaiImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan);
        Assert.Equal(source, result.PngBytes);
        // The default edit quality serves JPEG; the record names what arrived.
        Assert.Contains(result.Metadata.ProcessingHistory, line => line == "Returned image mime: image/jpeg");
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AnAccountListedImageModelEditsWithItsOwnIdInTheRequestAndProvenance()
    {
        const string listed = "grok-imagine-image-2.0-flash";
        var (scene, source, plan, intent) = Fixture(listed);
        using var handler = new Handler(request =>
        {
            using var json = JsonDocument.Parse(request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            Assert.Equal(listed, json.RootElement.GetProperty("model").GetString());
            return Image(Png(plan.Native), "image/png");
        });
        using var provider = new XaiImageProvider("unit-test-key", true, new CanvasCodec(source), handler, modelId: listed);
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan);
        Assert.Equal(listed, result.Metadata.ModelId);
        Assert.Equal(listed, result.Metadata.ModelVersion);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ADeclinedTierFallsBackOnceToTheDocumentedDefault()
    {
        const string listed = "grok-imagine-image-2.0-flash";
        var (scene, source, plan, intent) = Fixture(listed);
        var resolutions = new List<string?>();
        int calls = 0;
        using var handler = new Handler(request =>
        {
            calls++;
            using var json = JsonDocument.Parse(request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            var body = json.RootElement;
            resolutions.Add(body.TryGetProperty("resolution", out var tier) ? tier.GetString() : null);
            if (calls == 1)
                return new(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":{\"param\":\"resolution\"}}", Encoding.UTF8, "application/json") };
            return Image(Png(plan.Native), "image/png");
        });
        using var provider = new XaiImageProvider("unit-test-key", true, new CanvasCodec(source), handler, modelId: listed);
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan);
        // The biggest documented tier is attempted first; the one verified
        // fallback resubmits without it, at the documented default.
        Assert.Equal(new string?[] { "2k", null }, resolutions);
        Assert.Equal(2, handler.Calls);
        Assert.StartsWith("The account-listed model declined the 2k resolution tier", result.Metadata.ProcessingHistory[0]);
        Assert.Contains("default resolution tier", result.Metadata.ProcessingHistory[1]);
    }

    [Theory]
    [InlineData("model")]                                // another parameter is never a tier decline
    [InlineData(null)]                                   // a body without the documented error shape
    public async Task AmbiguousRejectionsNeverRetry(string? parameter)
    {
        const string listed = "grok-imagine-image-2.0-flash";
        var (scene, source, plan, intent) = Fixture(listed);
        string body = parameter is null ? "{}" : $"{{\"error\":{{\"param\":\"{parameter}\"}}}}";
        using var handler = new Handler(_ => new(HttpStatusCode.BadRequest) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        using var provider = new XaiImageProvider("unit-test-key", true, new CanvasCodec(source), handler, modelId: listed);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("xaiRejected", error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TheTestedProfilesOwnTierDeclineNeverRetries()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => new(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":{\"param\":\"resolution\"}}", Encoding.UTF8, "application/json") });
        using var provider = new XaiImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        // A rejection of the tested snapshot's documented contract is a failure to surface.
        Assert.Equal("xaiRejected", error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void TheDocumentedContractLocksTheCanvasToANamedRatio()
    {
        var size = new PixelSize(1222, 893);
        var plan = ResolutionPlanner.Plan(size, size, XaiImageProvider.AccountModel(XaiImageProvider.ModelId).Resolution);
        Assert.Equal(size, plan.Final);
        Assert.Equal(0, plan.Native.Width % 16);
        Assert.Equal(0, plan.Native.Height % 16);
        // The canvas sits at the documented ratio that carries the frame at full scale
        // with the least padding (here 1344x896, exact 3:2) —
        // and the frame keeps its own proportions inside it, never a reframing.
        Assert.Equal("3:2", XaiImageProvider.AspectRatioFor(plan.Native));
        Assert.Contains(XaiImageProvider.DocumentedRatios, entry =>
            (long)plan.Native.Width * entry.Ratio.Height == (long)plan.Native.Height * entry.Ratio.Width);
        Assert.InRange(plan.Sampling!.Value.Width / plan.Sampling.Value.Height, size.Width / (double)size.Height - 0.001, size.Width / (double)size.Height + 0.001);
    }

    [Fact]
    public async Task ARejectionSurfacesTheStructuredMachineType()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":{\"type\":\"invalid_request_error\",\"message\":\"reflects private prompt text\"}}", Encoding.UTF8, "application/json")
        });
        using var provider = new XaiImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Contains("HTTP 400 · invalid_request_error", error.Message);
        // The structured type is the one diagnostic that reaches the message; free text stays private.
        Assert.DoesNotContain("reflects private prompt text", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"name\":\"models/a-different-model\"}")]
    [InlineData("{\"data\":[{\"id\":123}]}")]
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
    [InlineData("{\"data\":[]}", "invalidResult")]
    [InlineData("{\"data\":[{\"url\":\"https://example.invalid/a.png\"}]}", "urlResultNotSupported")]
    [InlineData("{\"data\":[{\"b64_json\":\"not-base64\"}]}", "invalidResult")]
    [InlineData("{\"data\":[{\"b64_json\":\"QUJDRA==\",\"mime_type\":\"image/gif\"}]}", "invalidResult")]
    public async Task InvalidResultNeverFollowsUrlsOrRetries(string json, string code)
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        using var provider = new XaiImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(
            scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal(code, error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AnInlineResultWithAStrayUrlFieldUsesTheInlineImage()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[{\"b64_json\":\"" + Convert.ToBase64String(Png(plan.Native)) +
                "\",\"url\":\"https://example.invalid/a.png\"}]}", Encoding.UTF8, "application/json")
        });
        using var provider = new XaiImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        // The inline image the request asked for is right there; the stray url
        // is never fetched and fails nothing.
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan);
        Assert.Equal(source, result.PngBytes);
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
        using var provider = new XaiImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
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
        using var provider = new XaiImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("generationUnconfirmed", error.Code);
        Assert.DoesNotContain("sensitive-server-detail", error.Message);
        Assert.DoesNotContain("unit-test-key", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    private static RepresentationPreset Preset() => new() { Id = "test", Name = "Test", BaseInstruction = "Refine visible materials." };

    // The documented family contract plans the canvas; the listing's own id varies.
    private static (SceneManifest Scene, byte[] Png, ResolutionPlan Plan, RenderIntent Intent) Fixture(string modelId = XaiImageProvider.ModelId)
    {
        var scene = SceneFixture.Create();
        byte[] png = SceneFixture.Png;
        var plan = ResolutionPlanner.Plan(scene.Frame, scene.Frame, XaiImageProvider.AccountModel(modelId).Resolution);
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
    private static HttpResponseMessage Image(byte[] native, string mime) =>
        Json(new { data = new object[] { new { b64_json = Convert.ToBase64String(native), mime_type = mime } } });
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

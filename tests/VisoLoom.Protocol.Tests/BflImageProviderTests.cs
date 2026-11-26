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

public sealed class BflImageProviderTests
{
    [Fact]
    public void CloudOptInIsRequiredBeforeAnyNetworkOperation()
    {
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        Assert.Throws<ArgumentException>(() => new BflImageProvider("unit-test-key", false, new Codec(), handler));
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
            return Json(new { credits = 123 });
        });
        using var provider = new BflImageProvider("unit-test-key", true, new Codec(), handler);
        var capabilities = await provider.DiscoverAsync();
        Assert.Equal("bfl", capabilities.ProviderId);
        // No model list exists; the connection check validates the key, and
        // the two documented FLUX 3 profiles — the ones whose schemas accept
        // inline base64 — are offered, klein as the default row.
        Assert.Equal(new[] { BflImageProvider.ModelId, BflImageProvider.FlagshipModelId },
            capabilities.Models.Select(model => model.Id).ToArray());
        Assert.All(capabilities.Models, model =>
        {
            Assert.Equal(model.Id, model.Version);
            Assert.False(model.SupportsCancellation);
        });
        Assert.Equal("BFL · FLUX 3 Image Klein", capabilities.Models[0].DisplayName);
        Assert.Equal(BflImageProvider.KleinResolution, capabilities.Models[0].Resolution);
        Assert.Equal("BFL · FLUX 3 Image", capabilities.Models[1].DisplayName);
        Assert.Equal(BflImageProvider.FlagshipResolution, capabilities.Models[1].Resolution);
        Assert.Equal(BflImageProvider.KleinResolution.MaxPixels, capabilities.MaximumDecodedPixels);
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
        using var provider = new BflImageProvider("unit-test-key", true, new Codec(), handler);
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
        using var provider = new BflImageProvider("unit-test-key", true, new Codec(), handler);
        var failure = await Assert.ThrowsAsync<ProviderException>(() => provider.DiscoverAsync());
        Assert.Equal("bflRejected", failure.Code);
        Assert.DoesNotContain("private-key-detail", failure.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("flux-2-pro")]
    [InlineData("flux-2-pro-preview")]
    [InlineData("flux-2-klein-9b")]
    [InlineData("flux-3-image; drop table")]
    public void OnlyTheDocumentedFlux3ModelsAreSelectable(string modelId)
    {
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        Assert.Throws<ArgumentException>(() => new BflImageProvider("unit-test-key", true, new Codec(), handler, modelId));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void BothDocumentedFlux3ModelsConstructWithoutNetwork()
    {
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        using var klein = new BflImageProvider("unit-test-key", true, new Codec(), handler, BflImageProvider.ModelId);
        using var flagship = new BflImageProvider("unit-test-key", true, new Codec(), handler, BflImageProvider.FlagshipModelId);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task EditingSubmitsThePreparedReferenceAsInlineBase64AndPollsToDelivery()
    {
        var (scene, source, plan, intent) = Fixture();
        byte[] prepared = Png(plan.Native);
        var deliveryHost = "https://delivery.eu.bfl.ai/v1/results/task_1/sample.png?signature=abc";
        int poll = 0;
        using var handler = new Handler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("https://api.bfl.ai/v1/flux-3-image-klein", request.RequestUri!.AbsoluteUri);
                Assert.Equal("unit-test-key", request.Headers.GetValues("x-key").Single());
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
                using var body = JsonDocument.Parse(request.Content.ReadAsStringAsync().Result);
                var root = body.RootElement;
                // The documented klein request: prompt, the prepared reference
                // inline, the exact planned canvas, PNG output — and nothing else.
                Assert.Equal(5, root.EnumerateObject().Count());
                Assert.Equal(intent.EffectivePrompt, root.GetProperty("prompt").GetString());
                Assert.Equal(Convert.ToBase64String(prepared), root.GetProperty("images")[0].GetString());
                Assert.Equal(plan.Native.Width, root.GetProperty("width").GetInt32());
                Assert.Equal(plan.Native.Height, root.GetProperty("height").GetInt32());
                Assert.Equal("png", root.GetProperty("output_format").GetString());
                return Json(new { id = "task_1", polling_url = "https://api.bfl.ai/v1/get_result?id=task_1", cost = (double?)null });
            }
            if (request.RequestUri!.Host.StartsWith("delivery.", StringComparison.Ordinal))
            {
                // The signed delivery link carries the image and never the key.
                Assert.EndsWith(".bfl.ai", request.RequestUri.Host, StringComparison.Ordinal);
                Assert.False(request.Headers.Contains("x-key"));
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(source) { Headers = { ContentType = new("image/png") } } };
            }
            Assert.Equal("https://api.bfl.ai/v1/get_result?id=task_1", request.RequestUri.AbsoluteUri);
            Assert.Equal("unit-test-key", request.Headers.GetValues("x-key").Single());
            poll++;
            return poll switch
            {
                1 => Json(new { id = "task_1", status = "Pending", progress = (double?)null }),
                2 => Json(new { id = "task_1", status = "Generating", progress = 40.0 }),
                _ => Json(new { id = "task_1", status = "Ready", result = new { sample = deliveryHost }, progress = 100.0 })
            };
        });
        using var provider = new BflImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var stages = new List<ProviderProgress>();
        var result = await provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan,
            new Progress<ProviderProgress>(stages.Add));
        Assert.Equal(ContentDigest.Sha256(source), result.Metadata.OutputSha256);
        Assert.Equal("bfl", result.Metadata.ProviderId);
        Assert.Equal(BflImageProvider.ModelId, result.Metadata.ModelId);
        Assert.Equal(plan, result.Metadata.Plan);
        Assert.Equal(source, result.NativePngBytes);
        // The whole documented flow runs: submit, three polls, one delivery download.
        Assert.Equal(5, handler.Calls);
        Assert.Contains(stages, stage => stage.Stage == "queued");
        Assert.Contains(stages, stage => stage.Stage == "generating" && stage.Step == 40 && stage.Steps == 100);
        Assert.Contains(stages, stage => stage.Stage == "downloading");
        Assert.Contains(stages, stage => stage.Stage == "processing");
    }

    [Fact]
    public async Task TheFlagshipSubmitsWithoutOutputFormatOnItsOwnEndpoint()
    {
        var (scene, source, plan, intent) = Fixture(BflImageProvider.FlagshipModelId);
        byte[] prepared = Png(plan.Native);
        using var handler = new Handler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                // The flagship schema has no output_format field, so the request
                // carries exactly the four documented fields it does have.
                Assert.Equal("https://api.bfl.ai/v1/flux-3-image", request.RequestUri!.AbsoluteUri);
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
                var root = body.RootElement;
                Assert.Equal(4, root.EnumerateObject().Count());
                Assert.Equal(intent.EffectivePrompt, root.GetProperty("prompt").GetString());
                Assert.Equal(Convert.ToBase64String(prepared), root.GetProperty("images")[0].GetString());
                Assert.Equal(plan.Native.Width, root.GetProperty("width").GetInt32());
                Assert.Equal(plan.Native.Height, root.GetProperty("height").GetInt32());
                return Json(new { id = "task_1", polling_url = "https://api.bfl.ai/v1/get_result?id=task_1" });
            }
            if (request.RequestUri!.Host.StartsWith("delivery.", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(source) { Headers = { ContentType = new("image/png") } } };
            return Json(new { id = "task_1", status = "Ready", result = new { sample = "https://delivery.eu.bfl.ai/v1/results/task_1/sample.png" } });
        });
        using var provider = new BflImageProvider("unit-test-key", true, new CanvasCodec(source), handler,
            modelId: BflImageProvider.FlagshipModelId);
        var result = await provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan);
        Assert.Equal(BflImageProvider.FlagshipModelId, result.Metadata.ModelId);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task ARegionalPollingAddressOnBflsOwnDomainIsPolled()
    {
        // BFL states the returned polling_url "points at the region that holds
        // your task", so the host may be any BFL host; the rule under test is
        // BFL's own domain, and the fixture host name stands for that family.
        var (scene, source, plan, intent) = Fixture();
        string regional = "https://api.eu-central-1.bfl.ai/v1/get_result?id=task_1";
        using var handler = new Handler(request =>
        {
            if (request.Method == HttpMethod.Post)
                return Json(new { id = "task_1", polling_url = regional });
            if (request.RequestUri!.Host.StartsWith("delivery.", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(source) { Headers = { ContentType = new("image/png") } } };
            Assert.Equal(regional, request.RequestUri.AbsoluteUri);
            Assert.Equal("unit-test-key", request.Headers.GetValues("x-key").Single());
            return Json(new { id = "task_1", status = "Ready", result = new { sample = "https://delivery.us.bfl.ai/v1/results/task_1/sample.png" } });
        });
        using var provider = new BflImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var result = await provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan);
        Assert.Equal(ContentDigest.Sha256(source), result.Metadata.OutputSha256);
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData("Request Moderated")]
    [InlineData("Content Moderated")]
    public async Task AModeratedTaskFailsClosedWithNoFurtherRequests(string status)
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(request => request.Method == HttpMethod.Post
            ? Json(new { id = "task_1", polling_url = "https://api.bfl.ai/v1/get_result?id=task_1" })
            : Json(new { id = "task_1", status }));
        using var provider = new BflImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("moderated", error.Code);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("No Card Generated")]
    public async Task AFailedTaskReportsFailureWithoutRetry(string status)
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(request => request.Method == HttpMethod.Post
            ? Json(new { id = "task_1", polling_url = "https://api.bfl.ai/v1/get_result?id=task_1" })
            : Json(new { id = "task_1", status }));
        using var provider = new BflImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("bflFailed", error.Code);
        Assert.Contains("No automatic retry", error.Message);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task AnUnknownTaskIsReportedInsteadOfResubmitted()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(request => request.Method == HttpMethod.Post
            ? Json(new { id = "task_1", polling_url = "https://api.bfl.ai/v1/get_result?id=task_1" })
            : Json(new { id = "task_1", status = "Task not found" }));
        using var provider = new BflImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("taskUnknown", error.Code);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task APollingAddressOutsideTheDocumentedHostsIsNeverContacted()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => Json(new { id = "task_1", polling_url = "https://example.org/v1/get_result?id=task_1" }));
        using var provider = new BflImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("invalidResponse", error.Code);
        // The submit reached BFL; the foreign polling address never saw a request.
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ADeliveryLinkOutsideBflDeliveryIsNeverContacted()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(request => request.Method == HttpMethod.Post
            ? Json(new { id = "task_1", polling_url = "https://api.bfl.ai/v1/get_result?id=task_1" })
            : Json(new { id = "task_1", status = "Ready", result = new { sample = "https://files.example.org/image.png" } }));
        using var provider = new BflImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("invalidResult", error.Code);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task AnUnavailableDeliveryLinkReportsTheLinkNotTheImage()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(request => request.RequestUri!.Host.StartsWith("delivery.", StringComparison.Ordinal)
            ? new(HttpStatusCode.NotFound)
            : request.Method == HttpMethod.Post
                ? Json(new { id = "task_1", polling_url = "https://api.bfl.ai/v1/get_result?id=task_1" })
                : Json(new { id = "task_1", status = "Ready", result = new { sample = "https://delivery.eu.bfl.ai/v1/results/task_1/sample.png" } }));
        using var provider = new BflImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("invalidResult", error.Code);
        Assert.Contains("no longer available", error.Message);
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData("{\"id\":\"task_1\",\"status\":\"Ready\"}")]
    [InlineData("{\"id\":\"task_1\",\"status\":\"Ready\",\"result\":{}}")]
    [InlineData("{\"id\":\"task_1\",\"status\":\"Ready\",\"result\":{\"sample\":\"https://files.example.org/image.png\"},\"note\":\"a long provider remark that never reaches any user-facing message because the log only keeps labels\"}")]
    [InlineData("{\"id\":\"task_1\"}")]
    [InlineData("{\"id\":\"task_1\",\"status\":\"Serving\"}")]
    [InlineData("{\"id\":\"task_1\",\"status\":123}")]
    public async Task AReplyOutsideTheDocumentedShapeRecordsItsShapeAndNeverRetries(string json)
    {
        var (scene, source, plan, intent) = Fixture();
        string submit = "{\"id\":\"task_1\",\"polling_url\":\"https://api.bfl.ai/v1/get_result?id=task_1\"}";
        using var handler = new Handler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.OK) { Content = new StringContent(submit, Encoding.UTF8, "application/json") }
            : new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        using var provider = new BflImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("invalidResult", error.Code);
        Assert.DoesNotContain("never reaches", error.Message);
        Assert.NotNull(error.ResponseShapeDiagnostics);
        // Long strings become labels naming their length; bulk never rides.
        Assert.DoesNotContain("never reaches", error.ResponseShapeDiagnostics);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task StoppingTheWaitNeverResubmits()
    {
        var (scene, source, plan, intent) = Fixture();
        using var cts = new CancellationTokenSource();
        using var handler = new Handler(request =>
        {
            if (request.Method == HttpMethod.Post)
                return Json(new { id = "task_1", polling_url = "https://api.bfl.ai/v1/get_result?id=task_1" });
            cts.Cancel();
            return Json(new { id = "task_1", status = "Pending" });
        });
        using var provider = new BflImageProvider("unit-test-key", true, new CanvasCodec(source), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan,
            cancellationToken: cts.Token));
        Assert.Equal("generationUnconfirmed", error.Code);
        Assert.Contains("may still finish and be charged", error.Message);
        Assert.Contains("No automatic retry", error.Message);
        // The submission stands; a second POST is never made.
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task InvalidScenesAreRejectedBeforeAnyNetwork()
    {
        var scene = SceneFixture.Create();
        var empty = scene with { Passes = [] };
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        using var provider = new BflImageProvider("unit-test-key", true, new Codec(), handler);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.RenderAsync(empty,
            new Dictionary<string, byte[]> { ["reference.png"] = SceneFixture.Png }, new RenderIntent { RequestedOutput = scene.Frame },
            ResolutionPlanner.Plan(scene.Frame, scene.Frame, BflImageProvider.KleinResolution)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void TheDocumentedContractPlansACompleteFrameWithinTheDocumentedLimits()
    {
        var size = new PixelSize(1222, 893);
        // Klein: the 16-pixel alignment is VisoLoom's own planning choice;
        // the limits (each edge within [64, 6256], at most 16,822,272 pixels
        // together) are the documented klein contract.
        var klein = ResolutionPlanner.Plan(size, size, BflImageProvider.AccountModel(BflImageProvider.ModelId).Resolution);
        Assert.Equal(size, klein.Final);
        Assert.Equal(0, klein.Native.Width % 16);
        Assert.Equal(0, klein.Native.Height % 16);
        Assert.InRange(klein.Native.Width, 64, 6256);
        Assert.InRange(klein.Native.Height, 64, 6256);
        Assert.InRange(klein.Native.Pixels, 4_096, 16_822_272);
        // The flagship documents the same edges from 256 and the family's
        // 4 MP cap for reference-image edits.
        var flagship = ResolutionPlanner.Plan(size, size, BflImageProvider.AccountModel(BflImageProvider.FlagshipModelId).Resolution);
        Assert.Equal(size, flagship.Final);
        Assert.InRange(flagship.Native.Width, 256, 6256);
        Assert.InRange(flagship.Native.Height, 256, 6256);
        Assert.InRange(flagship.Native.Pixels, 65_536, 4_194_304);
    }

    private static RepresentationPreset Preset() => new() { Id = "test", Name = "Test", BaseInstruction = "Refine visible materials." };

    private static (SceneManifest Scene, byte[] Png, ResolutionPlan Plan, RenderIntent Intent) Fixture(
        string modelId = BflImageProvider.ModelId)
    {
        var scene = SceneFixture.Create();
        byte[] png = SceneFixture.Png;
        var plan = ResolutionPlanner.Plan(scene.Frame, scene.Frame, BflImageProvider.AccountModel(modelId).Resolution);
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

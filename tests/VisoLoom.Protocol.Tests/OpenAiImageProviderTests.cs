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

public sealed class OpenAiImageProviderTests
{
    [Fact]
    public void CloudOptInIsRequiredBeforeAnyNetworkOperation()
    {
        using var handler = new Handler(_ => throw new Exception("Network must not run."));
        Assert.Throws<ArgumentException>(() => new OpenAiImageProvider("unit-test-key", false, new Codec(), handler));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task DiscoveryMatchesTheAuthenticatedModelListAgainstThePinnedProfile()
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.openai.com/v1/models", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Content);
            return Json(new { data = new object[] { new { id = "unrelated-account-model" }, new { id = OpenAiImageProvider.ModelId } } });
        });
        using var provider = new OpenAiImageProvider("unit-test-key", true, new Codec(), handler);
        Assert.False((await provider.DiscoverAsync()).Models.Single().SupportsCancellation);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DiscoveryRejectsAnAccountWithoutAnyImageModel()
    {
        using var handler = new Handler(request => Json(new { data = new object[] { new { id = "gpt-some-other-model" } } }));
        using var provider = new OpenAiImageProvider("unit-test-key", true, new Codec(), handler);
        var failure = await Assert.ThrowsAsync<ProviderException>(() => provider.DiscoverAsync());
        Assert.Equal("modelUnavailable", failure.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DiscoveryRejectsAMalformedOrOversizedModelList()
    {
        using var oversized = new Handler(_ => Json(new { data = Enumerable.Range(0, 1025).Select(i => new { id = "openai-model-" + i }) }));
        using var provider = new OpenAiImageProvider("unit-test-key", true, new Codec(), oversized);
        await Assert.ThrowsAsync<ProviderException>(() => provider.DiscoverAsync());

        using var malformed = new Handler(_ => Json(new { models = Array.Empty<object>() }));
        using var rejected = new OpenAiImageProvider("unit-test-key", true, new Codec(), malformed);
        var failure = await Assert.ThrowsAsync<ProviderException>(() => rejected.DiscoverAsync());
        Assert.Equal("invalidResponse", failure.Code);
    }

    [Fact]
    public async Task DiscoveryOffersEveryAccountImageModelNewestFirst()
    {
        using var handler = new Handler(_ => Json(new
        {
            data = new object[]
            {
                new { id = "text-narrator-v3", created = 1_901_000_000 },
                new { id = OpenAiImageProvider.ModelId, created = 1_758_000_000 },
                new { id = "gpt-image-3-small-2027-08-01", created = 1_830_000_000 },
                new { id = "gpt-image-3-max-2028-01-15", created = 1_830_000_000 }
            }
        }));
        using var provider = new OpenAiImageProvider("unit-test-key", true, new Codec(), handler);
        var models = (await provider.DiscoverAsync()).Models;
        Assert.Equal(new[]
        {
            "gpt-image-3-max-2028-01-15",
            "gpt-image-3-small-2027-08-01",
            OpenAiImageProvider.ModelId
        }, models.Select(model => model.Id).ToArray());
        // The account list has no version field; the snapshot id is the version, as for the compiled profile.
        Assert.All(models, model => Assert.Equal(model.Id, model.Version));
    }

    [Fact]
    public async Task AnAccountWithoutTheCompiledSnapshotStillOffersItsImageModels()
    {
        using var handler = new Handler(_ => Json(new { data = new object[] { new { id = "gpt-image-3-max-2028-01-15", created = 1_830_000_000 } } }));
        using var provider = new OpenAiImageProvider("unit-test-key", true, new Codec(), handler);
        Assert.Equal("gpt-image-3-max-2028-01-15", (await provider.DiscoverAsync()).Models.Single().Id);
    }

    [Theory]
    [InlineData("gpt-image-1", "OpenAI · GPT Image 1")]
    [InlineData("gpt-image-1.5", "OpenAI · GPT Image 1.5")]
    [InlineData("gpt-image-2.5-sunburst", "OpenAI · GPT-Image-2.5 Sunburst")]
    [InlineData("gpt-image-3-max-2028-01-15", "OpenAI · gpt-image-3-max-2028-01-15")]
    [InlineData("gpt-image-2.6-sunburst", "OpenAI · GPT-Image-2.6 Sunburst")]
    [InlineData("gpt-image-2.6-sunburst-2027-01-15", "OpenAI · GPT-Image-2.6 Sunburst · 2027-01-15")]
    [InlineData("gpt-image-1-notice-me", "OpenAI · gpt-image-1-notice-me")]
    public void ResearchedModelsCarryTheirPageNamesOthersKeepRawIds(string id, string display)
    {
        // The recorded-naming rule: a model page's published name is used, a
        // dated snapshot of a researched base carries the base name plus its
        // own date, and everything else keeps its raw id.
        Assert.Equal(display, OpenAiImageProvider.AccountModel(id).DisplayName);
    }

    [Fact]
    public async Task ALaterSnapshotAttemptsTheReviewedCustomCanvasFirst()
    {
        const string later = "gpt-image-3-max-2028-01-15";
        var size = new PixelSize(1222, 893);
        byte[] source = Png(size);
        var (fixture, _, _, _) = Fixture();
        var scene = fixture with
        {
            Frame = size,
            Camera = fixture.Camera with { ViewportPixels = size },
            Passes = [fixture.Passes[0] with { Size = size, ByteCount = source.LongLength, Sha256 = ContentDigest.Sha256(source) }]
        };
        var plan = ResolutionPlanner.Plan(size, size, OpenAiImageProvider.AccountModel(later).Resolution);
        var intent = ScenePromptComposer.Compose(scene, Preset(), "", plan, false);
        string requested = "";
        using var handler = new Handler(request =>
        {
            using var json = JsonDocument.Parse(request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            Assert.Equal(later, json.RootElement.GetProperty("model").GetString());
            requested = json.RootElement.GetProperty("size").GetString()!;
            return Image(Png(plan.Native));
        });
        using var provider = new OpenAiImageProvider("unit-test-key", true, new CanvasCodec(), handler, modelId: later);
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan);
        Assert.Equal($"{plan.Native.Width}x{plan.Native.Height}", requested);
        Assert.Equal(plan.Native, result.Metadata.Plan.Native);
        Assert.Equal(later, result.Metadata.ModelId);
        Assert.Equal(plan.Native, PngHeader.Read(result.NativePngBytes!));
    }

    [Fact]
    public async Task ADeclinedCustomCanvasFallsBackOnceToTheStandardSet()
    {
        const string later = "gpt-image-3-max-2028-01-15";
        var size = new PixelSize(1222, 893);
        byte[] source = Png(size);
        var (fixture, _, _, _) = Fixture();
        var scene = fixture with
        {
            Frame = size,
            Camera = fixture.Camera with { ViewportPixels = size },
            Passes = [fixture.Passes[0] with { Size = size, ByteCount = source.LongLength, Sha256 = ContentDigest.Sha256(source) }]
        };
        var plan = ResolutionPlanner.Plan(size, size, OpenAiImageProvider.AccountModel(later).Resolution);
        var intent = ScenePromptComposer.Compose(scene, Preset(), "", plan, false);
        var baseline = ResolutionPlanner.Plan(size, size, OpenAiImageProvider.FamilyResolution);
        Assert.Contains(baseline.Native, new[] { new PixelSize(1024, 1024), new(1024, 1536), new(1536, 1024) });
        var requestedSizes = new List<string>();
        using var handler = new Handler(request =>
        {
            using var json = JsonDocument.Parse(request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            requestedSizes.Add(json.RootElement.GetProperty("size").GetString()!);
            if (requestedSizes.Count == 1)
                return new(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        error = new
                        {
                            message = $"Unsupported size for {later}.",
                            type = "invalid_request_error",
                            param = "size",
                            code = "unsupported_size"
                        }
                    }), Encoding.UTF8, "application/json")
                };
            return Image(Png(baseline.Native));
        });
        using var provider = new OpenAiImageProvider("unit-test-key", true, new CanvasCodec(), handler, modelId: later);
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan);
        Assert.Equal(2, handler.Calls);
        Assert.Equal([$"{plan.Native.Width}x{plan.Native.Height}", $"{baseline.Native.Width}x{baseline.Native.Height}"], requestedSizes);
        Assert.Equal(baseline, result.Metadata.Plan);
        Assert.Equal(size, PngHeader.Read(result.PngBytes));
        Assert.Equal(baseline.Native, PngHeader.Read(result.NativePngBytes!));
        Assert.Contains("declined the reviewed custom canvas", result.Metadata.ProcessingHistory[0]);
        Assert.Contains("standard canvas set", result.Metadata.ProcessingHistory[0]);
    }

    [Fact]
    public async Task AmbiguousOrTestedProfileSizeRejectionsNeverRetry()
    {
        var (fixture, source, plan, intent) = Fixture();
        HttpResponseMessage rejected = new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            { error = new { message = "The prompt exceeds limits.", type = "invalid_request_error", param = "prompt" } }),
            Encoding.UTF8, "application/json")
        };
        using var unrelated = new Handler(_ => rejected);
        using var later = new OpenAiImageProvider("unit-test-key", true, new Codec(), unrelated, modelId: "gpt-image-3-max-2028-01-15");
        var failure = await Assert.ThrowsAsync<ProviderException>(() =>
            later.RenderAsync(fixture, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("openaiRejected", failure.Code);
        Assert.Equal(1, unrelated.Calls);

        using var opaque = new Handler(_ => new(HttpStatusCode.BadRequest) { Content = new StringContent("gatekeeper page", Encoding.UTF8, "text/html") });
        using var unlisted = new OpenAiImageProvider("unit-test-key", true, new Codec(), opaque, modelId: "gpt-image-3-max-2028-01-15");
        var unreadable = await Assert.ThrowsAsync<ProviderException>(() =>
            unlisted.RenderAsync(fixture, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("openaiRejected", unreadable.Code);
        Assert.Equal(1, opaque.Calls);

        // The compiled profile's contract is reviewed; its rejections never fall back.
        using var tested = new Handler(_ => new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            { error = new { message = "Unsupported size.", type = "invalid_request_error", param = "size" } }),
            Encoding.UTF8, "application/json")
        });
        using var provider = new OpenAiImageProvider("unit-test-key", true, new Codec(), tested);
        var reviewed = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.RenderAsync(fixture, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("openaiRejected", reviewed.Code);
        Assert.Equal(1, tested.Calls);
    }

    private sealed class CanvasCodec : IImageProcessor
    {
        public byte[] PrepareReference(byte[] png, ResolutionPlan plan) => Png(plan.Native);
        public byte[] NormalizeResult(byte[] png, ResolutionPlan plan) => Png(plan.Final);
    }

    [Fact]
    public async Task EditSendsOneReferenceAndTheExactRecordedPromptAndReturnsValidatedProvenance()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(request =>
        {
            Assert.Equal("https://api.openai.com/v1/images/edits", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            using var json = JsonDocument.Parse(request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            var body = json.RootElement;
            Assert.Equal(OpenAiImageProvider.ModelId, body.GetProperty("model").GetString());
            Assert.Equal(intent.EffectivePrompt, body.GetProperty("prompt").GetString());
            Assert.Equal(1, body.GetProperty("images").GetArrayLength());
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(source), body.GetProperty("images")[0].GetProperty("image_url").GetString());
            Assert.Equal("1024x1024", body.GetProperty("size").GetString());
            Assert.Equal("png", body.GetProperty("output_format").GetString());
            Assert.Equal(1, body.GetProperty("n").GetInt32());
            Assert.False(body.TryGetProperty("scene", out _));
            Assert.False(body.TryGetProperty("input_fidelity", out _));
            return Image(source);
        });
        using var provider = new OpenAiImageProvider("unit-test-key", true, new Codec(), handler);
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan);
        Assert.Equal(source, result.PngBytes);
        Assert.False(result.Metadata.IsUnchangedReference);
        Assert.Equal(scene.SceneId, result.Metadata.SceneId);
        Assert.Equal(intent.DisclosedContextFields, result.Metadata.DisclosedContextFields);
        Assert.Equal(ContentDigest.Sha256(source), result.Metadata.OutputSha256);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{invalid-json")]
    [InlineData("[]")]
    [InlineData("{\"id\":123}")]
    [InlineData("{\"id\":\"a-different-model\"}")]
    public async Task InvalidDiscoveryFailsSafelyWithoutChoosingAnotherModel(string json)
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        using var provider = new OpenAiImageProvider("unit-test-key", true, new Codec(), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.DiscoverAsync());
        Assert.DoesNotContain(json, error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"data\":[null]}")]
    [InlineData("{\"data\":[{\"url\":\"https://example.invalid/image\"}]}")]
    [InlineData("{\"data\":[{\"b64_json\":\"not-base64\"}]}")]
    public async Task InvalidResultNeverFollowsUrlsOrRetries(string json)
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        using var provider = new OpenAiImageProvider("unit-test-key", true, new Codec(), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(
            scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("invalidResult", error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task PaddingPlanUsesThePreparedCanvasAndNormalizesBackToTheRequestedFrame()
    {
        var (scene, _, _, _) = Fixture();
        var size = new PixelSize(1023, 1023);
        byte[] source = Png(size);
        scene = scene with
        {
            Frame = size,
            Camera = scene.Camera with { ViewportPixels = size },
            Passes = [scene.Passes[0] with { Size = size, ByteCount = source.LongLength, Sha256 = ContentDigest.Sha256(source) }]
        };
        var plan = ResolutionPlanner.Plan(size, size, OpenAiImageProvider.Model.Resolution);
        var intent = ScenePromptComposer.Compose(scene, Preset(), "", plan, false);
        byte[] prepared = Png(plan.Native);
        var codec = new PlannedCodec(prepared, source, plan);
        using var handler = new Handler(request =>
        {
            using var json = JsonDocument.Parse(request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(prepared),
                json.RootElement.GetProperty("images")[0].GetProperty("image_url").GetString());
            return Image(prepared);
        });
        using var provider = new OpenAiImageProvider("unit-test-key", true, codec, handler);
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan);
        Assert.Equal(size, PngHeader.Read(result.PngBytes));
        Assert.Equal(plan, result.Metadata.Plan);
        Assert.Equal(prepared, result.NativePngBytes);
        Assert.True(codec.Normalized);
    }

    [Fact]
    public async Task AmbiguousSubmissionNeverRetriesOrIncludesSecretInError()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => throw new HttpRequestException("sensitive-server-detail"));
        using var provider = new OpenAiImageProvider("unit-test-key", true, new Codec(), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("generationUnconfirmed", error.Code);
        Assert.DoesNotContain("sensitive-server-detail", error.Message);
        Assert.DoesNotContain("unit-test-key", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ReviewedCanvasAndPromptAreSubmittedWithoutPreparingAnotherImage()
    {
        var (scene, _, _, _) = Fixture();
        var frame = new PixelSize(1023, 1023);
        byte[] source = Png(frame);
        scene = scene with { Frame = frame, Camera = scene.Camera with { ViewportPixels = frame },
            Passes = [scene.Passes[0] with { Size = frame, ByteCount = source.LongLength, Sha256 = ContentDigest.Sha256(source) }] };
        var plan = ResolutionPlanner.Plan(frame, frame, OpenAiImageProvider.Model.Resolution);
        var intent = ScenePromptComposer.Compose(scene, Preset(), "Reviewed intent", plan, false);
        byte[] reviewed = Png(plan.Native);
        using var handler = new Handler(request =>
        {
            using var json = JsonDocument.Parse(request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            Assert.Equal(intent.EffectivePrompt, json.RootElement.GetProperty("prompt").GetString());
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(reviewed),
                json.RootElement.GetProperty("images")[0].GetProperty("image_url").GetString());
            return Image(reviewed);
        });
        using var provider = new OpenAiImageProvider("unit-test-key", true, new ReviewedCodec(source), handler);
        var result = await provider.RenderPreparedAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan, reviewed);
        Assert.Equal(source, result.PngBytes);
        Assert.Equal(reviewed, result.NativePngBytes);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task InvalidReviewedCanvasOrSourceNeverSubmits()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => throw new Exception("Must not submit."));
        using var provider = new OpenAiImageProvider("unit-test-key", true, new ReviewedCodec(source), handler);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.RenderPreparedAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan, SceneFixture.Png));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.RenderPreparedAsync(scene,
            new Dictionary<string, byte[]> { ["reference.png"] = SceneFixture.Png }, intent, plan, source));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostCodecFailureBeforeSubmissionSendsNoImage(bool missingMethod)
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => throw new Exception("Must not submit."));
        Exception failure = missingMethod ? new MissingMethodException("private-detail") : new NotSupportedException("private-detail");
        using var provider = new OpenAiImageProvider("unit-test-key", true, new FailingCodec(failure, false), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(
            scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("imagePreparationFailed", error.Code);
        Assert.Contains("No image was sent", error.Message);
        Assert.DoesNotContain("private-detail", error.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task HostCodecFailureAfterGenerationReportsPossibleChargeWithoutRetrying()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => Image(source));
        using var provider = new OpenAiImageProvider("unit-test-key", true, new FailingCodec(new MissingMethodException("private-detail"), true), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(
            scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("imageNormalizationFailed", error.Code);
        Assert.Contains("may have been charged", error.Message);
        Assert.DoesNotContain("private-detail", error.Message);
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
        using var provider = new OpenAiImageProvider("unit-test-key", true, new Codec(), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.DoesNotContain("private-prompt-and-key", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task WrongDimensionsAreRejectedBeforeDisplay()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => Image(SceneFixture.Png));
        using var provider = new OpenAiImageProvider("unit-test-key", true, new Codec(), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan));
        Assert.Equal("invalidResult", error.Code);
    }

    [Fact]
    public async Task TamperedReferenceAndCancelledPreflightNeverSubmit()
    {
        var (scene, source, plan, intent) = Fixture();
        using var handler = new Handler(_ => throw new Exception("Must not submit."));
        using var provider = new OpenAiImageProvider("unit-test-key", true, new Codec(), handler);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = SceneFixture.Png }, intent, plan));
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = source }, intent, plan, cancellationToken: ct.Token));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void NamesAndIdentifiersAreOmittedUnlessNamesAreExplicitlySelected()
    {
        var (scene, _, plan, _) = Fixture();
        var materialId = Guid.NewGuid();
        scene = scene with
        {
            Materials = [new() { Id = materialId, Name = "private-material-name", Description = "private-material-description", BaseColorSrgb = "#AAAAAA" }],
            Layers = [new() { Id = Guid.NewGuid(), Name = "private-layer-name" }]
        };
        var anonymous = ScenePromptComposer.Compose(scene, Preset(), "My intent", plan, false);
        Assert.DoesNotContain("private-", anonymous.EffectivePrompt);
        Assert.DoesNotContain(materialId.ToString(), anonymous.EffectivePrompt);
        Assert.Contains("#AAAAAA", anonymous.EffectivePrompt);
        Assert.Contains(PreservationRules.Objective, anonymous.EffectivePrompt);
        var named = ScenePromptComposer.Compose(scene, Preset(), "My intent", plan, true);
        Assert.Contains("private-layer-name", named.EffectivePrompt);
        Assert.Contains("materialNames", named.DisclosedContextFields);
        Assert.Contains("layerNames", named.DisclosedContextFields);
        Assert.DoesNotContain(materialId.ToString(), named.EffectivePrompt);
    }

    [Fact]
    public void GridPlanKeepsTheCompleteOddSizedFrameInsideTheModelCanvas()
    {
        var size = new PixelSize(2358, 1810);
        var plan = ResolutionPlanner.Plan(size, size, OpenAiImageProvider.Model.Resolution);
        Assert.Equal(size, plan.Final);
        Assert.Equal(size.Width, plan.ContentRectangle.Width);
        Assert.Equal(size.Height, plan.ContentRectangle.Height);
        Assert.Equal(0, plan.Native.Width % 16);
        Assert.Equal(0, plan.Native.Height % 16);
        Assert.Equal(1, plan.Scale);
        Assert.True(plan.HasPadding);
        Assert.False(ResolutionPlanner.Supports(new(3072, 256), OpenAiImageProvider.Model.Resolution));
    }

    private static RepresentationPreset Preset() => new() { Id = "test", Name = "Test", BaseInstruction = "Refine visible materials." };
    private static (SceneManifest Scene, byte[] Png, ResolutionPlan Plan, RenderIntent Intent) Fixture()
    {
        var size = new PixelSize(1024, 1024);
        byte[] png = Png(size);
        var original = SceneFixture.Create();
        var scene = original with
        {
            Frame = size,
            Camera = original.Camera with { ViewportPixels = size, Frustum = new(-1, 1, -1, 1, 1, 100) },
            Passes = [original.Passes[0] with { Size = size, ByteCount = png.LongLength, Sha256 = ContentDigest.Sha256(png) }]
        };
        var plan = ResolutionPlanner.Plan(size, size, OpenAiImageProvider.Model.Resolution);
        return (scene, png, plan, ScenePromptComposer.Compose(scene, Preset(), "", plan, false));
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

    private sealed class ReviewedCodec(byte[] final) : IImageProcessor
    {
        public byte[] PrepareReference(byte[] png, ResolutionPlan plan) => throw new Exception("A reviewed canvas must not be prepared again.");
        public byte[] NormalizeResult(byte[] png, ResolutionPlan plan) => final;
    }

    private sealed class FailingCodec(Exception failure, bool afterGeneration) : IImageProcessor
    {
        public byte[] PrepareReference(byte[] png, ResolutionPlan plan) => afterGeneration ? png : throw failure;
        public byte[] NormalizeResult(byte[] png, ResolutionPlan plan) => throw failure;
    }

    private sealed class PlannedCodec(byte[] prepared, byte[] final, ResolutionPlan expected) : IImageProcessor
    {
        public bool Normalized { get; private set; }
        public byte[] PrepareReference(byte[] png, ResolutionPlan plan)
        {
            Assert.Equal(expected, plan);
            return prepared;
        }
        public byte[] NormalizeResult(byte[] png, ResolutionPlan plan)
        {
            Assert.Equal(expected, plan);
            Assert.Equal(prepared, png);
            Normalized = true;
            return final;
        }
    }
}

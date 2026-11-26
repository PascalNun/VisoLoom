using System.Text;
using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;
using VisoLoom.Providers;
using VisoLoom.Tests;
using Xunit;

namespace VisoLoom.Core.Tests;

public sealed class ContractTests
{
    [Fact]
    public void LocalDiscoveryUsesTheActiveReviewedProfileAndRejectsAmbiguity()
    {
        var capabilities = ReferenceCapabilities.Create();
        var captureTest = Assert.Single(capabilities.Models);
        var flux2 = ManagedModels.KleinFourBit.Create("mflux-test");
        var flux3 = ManagedModels.KleinFourKLocal.Create("sdcpp-test");

        Assert.Empty(LocalProviderClient.ActiveManagedModels(capabilities));
        var activeFlux2 = Assert.Single(LocalProviderClient.ActiveManagedModels(capabilities with { Models = [captureTest, flux2] }));
        var activeFlux3 = Assert.Single(LocalProviderClient.ActiveManagedModels(capabilities with { Models = [captureTest, flux3] }));
        Assert.Equal((flux2.Id, flux2.Version, flux2.Resolution), (activeFlux2.Id, activeFlux2.Version, activeFlux2.Resolution));
        Assert.Equal((flux3.Id, flux3.Version, flux3.Resolution), (activeFlux3.Id, activeFlux3.Version, activeFlux3.Resolution));
        // Both installed models the engine validated are selectable together.
        var both = LocalProviderClient.ActiveManagedModels(capabilities with { Models = [captureTest, flux2, flux3] });
        Assert.Equal([flux2.Id, flux3.Id], both.Select(model => model.Id).ToArray());
        var wrongCanvas = flux3 with { Resolution = flux2.Resolution };
        Assert.Empty(LocalProviderClient.ActiveManagedModels(capabilities with { Models = [captureTest, wrongCanvas] }));
    }

    [Theory]
    [InlineData(2358, 1810)]
    [InlineData(3001, 1700)]
    [InlineData(431, 997)]
    public void SmallLocalCanvasRetainsCompleteFrameAtOneUniformScale(int width, int height)
    {
        var size = new PixelSize(width, height);
        var limits = ManagedModels.KleinFourBit.Create("fixture").Resolution;
        var plan = ResolutionPlanner.Plan(size, size, limits);
        Assert.True(ResolutionPlanner.Supports(plan.Native, limits));
        Assert.NotNull(plan.Sampling);
        var content = plan.Sampling.Value;
        Assert.Equal((double)width / height, content.Width / content.Height, 12);
        Assert.InRange(content.X, 0, plan.Native.Width);
        Assert.InRange(content.Y, 0, plan.Native.Height);
        Assert.InRange(content.X + content.Width, 0, plan.Native.Width + 1e-9);
        Assert.InRange(content.Y + content.Height, 0, plan.Native.Height + 1e-9);
        foreach (var point in new[] { (0.5, 0.5), (width - 0.5, height - 0.5), (width / 2.0, height / 2.0) })
        {
            var native = plan.ToNative(point.Item1, point.Item2);
            var restored = plan.ToCapture(native.X, native.Y);
            Assert.Equal(point.Item1, restored.X, 9);
            Assert.Equal(point.Item2, restored.Y, 9);
        }
        Assert.Equal(plan, JsonSerializer.Deserialize<ResolutionPlan>(JsonSerializer.Serialize(plan)));
    }

    [Fact]
    public void LocalPromptOmitsSceneJsonKeepsAuthorshipAndRejectsOverflow()
    {
        var preset = new RepresentationPreset { Id = "fixture", Name = "Sketch", BaseInstruction = "Draw a light sketch." };
        string user = "Keep the existing blue upholstery.";
        var intent = ManagedModels.KleinFourBit.Compose(preset, user, new(512, 448));
        Assert.Contains(user, intent.EffectivePrompt);
        Assert.Equal(2, intent.EffectivePrompt.Split(PreservationRules.Objective).Length);
        Assert.Empty(intent.DisclosedContextFields);
        Assert.DoesNotContain("camera", intent.EffectivePrompt.Replace(PreservationRules.Objective, ""));
        Assert.Throws<ArgumentException>(() => ManagedModels.KleinFourBit.Compose(preset, new string('界', 1000), new(512, 448)));
    }

    private static ResolutionConstraints Limits => new()
    {
        IsKnown = true,
        MinEdge = 1,
        MaxEdge = 8192,
        MinPixels = 1,
        MaxPixels = 16_777_216
    };

    [Theory]
    [InlineData(2438, 1804, 1920, 1421)]
    [InlineData(2358, 1810, 1200, 921)]
    [InlineData(431, 997, 100, 231)]
    [InlineData(5000, 1000, 1002, 200)]
    [InlineData(1000, 5000, 200, 1000)]
    public void ExportWidthRetainsTypedValueWithoutAnAspectOrModelGrid(int w, int h, int requested, int expectedHeight)
    {
        var viewport = new PixelSize(w, h);
        var output = ResolutionPlanner.OutputWidth(viewport, requested);
        Assert.Equal(new(requested, expectedHeight), output);
        var capture = ResolutionPlanner.CaptureSize(viewport, output);
        Assert.Equal((long)w * capture.Height, (long)h * capture.Width);
        var plan = ResolutionPlanner.Plan(capture, output, ManagedModels.KleinFourBit.Resolution);
        Assert.Equal(output, plan.Final);
        var fit = ResolutionPlanner.FitCanvas(capture, output);
        Assert.InRange(fit.X, 0, output.Width / 2.0);
        Assert.InRange(fit.Y, 0, output.Height / 2.0);
        Assert.Equal((double)w / h, fit.Width / fit.Height, 10);
        var transposed = ResolutionPlanner.OutputHeight(new(h, w), requested);
        Assert.Equal(new(expectedHeight, requested), transposed);
    }

    [Fact]
    public void LargeExportsDoNotBypassTheRhinoCaptureLimitOrModelLimits()
    {
        var viewport = new PixelSize(2438, 1804);
        var output = ResolutionPlanner.ScaleViewport(viewport, 2);
        Assert.True(output.Pixels > ResolutionPlanner.CapturePixelLimit);
        Assert.Equal(viewport, ResolutionPlanner.CaptureSize(viewport, output));
        var plan = ResolutionPlanner.Plan(viewport, output, ManagedModels.KleinFourBit.Resolution);
        Assert.True(ResolutionPlanner.Supports(plan.Native, ManagedModels.KleinFourBit.Resolution));
        Assert.Equal(output, plan.Final);
        Assert.Throws<ArgumentException>(() => ResolutionPlanner.ValidateSize(output));
        Assert.Throws<ArgumentException>(() => ResolutionPlanner.ScaleViewport(viewport, 4));
        Assert.Throws<ArgumentException>(() => ResolutionPlanner.OutputWidth(viewport, 16000));
        Assert.Throws<ArgumentOutOfRangeException>(() => ResolutionPlanner.OutputWidth(viewport, 0));
    }

    [Fact]
    public void LargerExportHeadersRemainRejectedAsCaptureInputs()
    {
        var png = SceneFixture.Png;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), 5000);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20, 4), 3661);
        Assert.Throws<ArgumentException>(() => PngHeader.Read(png));
        Assert.Equal(new(5000, 3661), PngHeader.ReadExport(png));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), 16384);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20, 4), 5000);
        Assert.Throws<ArgumentException>(() => PngHeader.ReadExport(png));
    }

    [Fact]
    public void SmallerExportTargetsASmallerModelCanvasBeforeMinimisingPadding()
    {
        var plan = ResolutionPlanner.Plan(new(1024, 768), new(800, 600), ManagedModels.KleinFourBit.Resolution);
        Assert.Equal(new(832, 640), plan.Native);
        Assert.Equal(new(800, 600), plan.Final);
    }

    [Fact]
    public void LinkedSizeRejectsInvalidRequestsAndExcessiveArea()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ResolutionPlanner.OutputWidth(new(1920, 1080), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ResolutionPlanner.OutputHeight(new(1920, 1080), 16385));
        Assert.Throws<ArgumentException>(() => ResolutionPlanner.OutputWidth(new(1920, 1080), 16000));
        Assert.Throws<ArgumentException>(() => ResolutionPlanner.OutputWidth(new(0, 1080), 1920));
    }

    [Theory]
    [InlineData(3000, 1700, 3008, 1712, 4, 6, 3000, 1700)]
    [InlineData(3840, 2160, 3840, 2160, 0, 0, 3840, 2160)]
    public void GridPreservesCompleteFrame(int w, int h, int nw, int nh, int x, int y, int cw, int ch)
    {
        var plan = ResolutionPlanner.Plan(new(w, h), new(w, h), Limits with { DimensionMultiple = 16 });
        Assert.Equal(new PixelSize(nw, nh), plan.Native);
        Assert.Equal(new PixelRectangle(x, y, cw, ch), plan.ContentRectangle);
        var native = plan.ToNative(0.5, h - 0.5);
        var roundtrip = plan.ToCapture(native.X, native.Y);
        Assert.Equal(0.5, roundtrip.X, 6);
        Assert.Equal(h - 0.5, roundtrip.Y, 6);
    }

    [Theory]
    [InlineData(3840, 2160, 0, 80, 1536, 864)]
    [InlineData(3000, 1700, 3, 78, 1530, 867)]
    public void FixedCanvasUsesIntegerAspectRectangle(int w, int h, int x, int y, int cw, int ch)
    {
        var plan = ResolutionPlanner.Plan(new(w, h), new(w, h), Limits with { FixedSizes = [new(1536, 1024)] });
        Assert.Equal(new PixelRectangle(x, y, cw, ch), plan.ContentRectangle);
        Assert.True(plan.RequiresResampling);
        Assert.Equal((long)w * ch, (long)h * cw);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(431, 997)]
    [InlineData(3001, 101)]
    [InlineData(101, 3001)]
    public void ExactSizesDoNotChangeComposition(int width, int height)
    {
        var size = new PixelSize(width, height);
        Assert.True(ResolutionPlanner.Plan(size, size, Limits).IsExact);
    }

    [Fact]
    public void AllowedRatiosAndMinimumAreaFindAContainingCanvas()
    {
        var ratioPlan = ResolutionPlanner.Plan(new(1920, 1080), new(1920, 1080), Limits with
        {
            DimensionMultiple = 16,
            AllowedRatios = [new(16, 9)]
        });
        Assert.Equal(new PixelSize(2048, 1152), ratioPlan.Native);
        var areaPlan = ResolutionPlanner.Plan(new(100, 100), new(100, 100), Limits with
        {
            MinPixels = 20000,
            MaxEdge = 200
        });
        Assert.Equal(20000, areaPlan.Native.Pixels);
        Assert.Equal(1, areaPlan.Scale);
    }

    [Fact]
    public void AuthoredSceneAndPresetFilesLoadThroughWireContract()
    {
        var example = Path.Combine(AppContext.BaseDirectory, "Examples");
        var scene = SubmissionValidator.Deserialize<SceneManifest>(File.ReadAllBytes(Path.Combine(example, "scene.json")));
        Assert.Empty(SceneValidator.Validate(scene));
        Assert.Equal(scene.Passes[0].Sha256, ContentDigest.Sha256(File.ReadAllBytes(Path.Combine(example, "reference.png"))));
        var presets = Directory.GetFiles(Path.Combine(example, "presets"), "*.json");
        Assert.Equal(7, presets.Length);
        foreach (var file in presets)
        {
            var preset = SubmissionValidator.Deserialize<RepresentationPreset>(File.ReadAllBytes(file));
            Assert.Contains(PreservationRules.Objective, PromptComposer.Compose(preset, "").EffectivePrompt);
        }
    }

    [Fact]
    public void InvalidResolutionIsRejected()
    {
        Assert.Throws<ArgumentException>(() => ResolutionPlanner.Plan(new(1920, 1080), new(4096, 4096), Limits));
        Assert.Throws<ArgumentException>(() => ResolutionPlanner.Plan(new(1920, 1080), new(1920, 1080), Limits with { IsKnown = false }));
        Assert.Throws<OverflowException>(() => ResolutionPlanner.ScaleViewport(new(int.MaxValue, 1), 4));
        Assert.Throws<ArgumentException>(() => ResolutionPlanner.Plan(new(3001, 1700), new(3001, 1700), Limits with { FixedSizes = [new(512, 512)] }));
    }

    [Fact]
    public void SceneRoundTripIsValidAndDoesNotContainPaths()
    {
        var scene = SceneFixture.Create();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options);
        var decoded = SubmissionValidator.Deserialize<SceneManifest>(bytes);
        Assert.Empty(SceneValidator.Validate(decoded));
        Assert.Equal(scene.Camera, decoded.Camera);
        Assert.DoesNotContain("/Users/", Encoding.UTF8.GetString(bytes));
        Assert.Equal(scene.Frame, PngHeader.Read(SceneFixture.Png));
    }

    [Fact]
    public void SceneRejectsBrokenReferencesAndUnavailableAssetClaims()
    {
        var scene = SceneFixture.Create();
        Assert.NotEmpty(SceneValidator.Validate(scene with { SchemaVersion = "2.0" }));
        Assert.NotEmpty(SceneValidator.Validate(scene with { RequiredFeatures = ["unknownFeature"] }));
        Assert.NotEmpty(SceneValidator.Validate(scene with
        {
            Objects = [
                new() {
                    Id = Guid.NewGuid(),
                    LayerId = Guid.NewGuid(),
                    ObjectType = "Mesh"
                }
            ]
        }));
        Assert.NotEmpty(SceneValidator.Validate(scene with { Passes = [scene.Passes[0], scene.Passes[0]] }));
        Assert.NotEmpty(SceneValidator.Validate(scene with { Passes = [scene.Passes[0] with { FileName = "../reference.png" }] }));
        Assert.NotEmpty(SceneValidator.Validate(scene with { Camera = scene.Camera with { Position = new(double.NaN, 0, 0) } }));
    }

    [Fact]
    public void AProviderCanvasRestoresOnlyAsTheSameDrawingRescaled()
    {
        // The incident case: a 2,048 × 1,808 return against a planned 2,048 × 1,516 canvas
        // reframes the drawing — no rescale can carry those pixels back into the Rhino frame.
        Assert.Null(ResolutionPlanner.ProviderCanvasScale(new(2048, 1516), new(2048, 1808)));
        // The provider's own extent at the planned aspect is the same drawing rescaled.
        var uniform = ResolutionPlanner.ProviderCanvasScale(new(2432, 1824), new(1820, 1365));
        Assert.NotNull(uniform);
        Assert.Equal(uniform.Value.X, uniform.Value.Y, 9);
        // Provider rounding stays inside the tolerance; anything beyond reframes.
        Assert.NotNull(ResolutionPlanner.ProviderCanvasScale(new(1000, 1000), new(981, 1000)));
        Assert.Null(ResolutionPlanner.ProviderCanvasScale(new(1000, 1000), new(979, 1000)));
        Assert.Null(ResolutionPlanner.ProviderCanvasScale(new(1000, 1000), new(0, 500)));
    }

    [Fact]
    public void SceneInstanceTableResolvesPathsAndMatrices()
    {
        var scene = SceneFixture.Create();
        var outer = Guid.NewGuid();
        var nested = Guid.NewGuid();
        scene = scene with
        {
            Instances =
            [
                new() { InstanceId = outer, DefinitionId = Guid.NewGuid(), DefinitionName = "Tree", Xform = Identity, Scale = new(1, 1, 1) },
                new() { InstanceId = nested, DefinitionId = Guid.NewGuid(), DefinitionName = "Crown", Xform = Identity, Scale = new(2, 2, 2), Path = [outer] }
            ]
        };
        Assert.Empty(SceneValidator.Validate(scene));
        var decoded = SubmissionValidator.Deserialize<SceneManifest>(JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options));
        Assert.Empty(SceneValidator.Validate(decoded));
        // List members do not round-trip by record equality, so each placement keeps its own values.
        Assert.Equal(scene.Instances.Count, decoded.Instances.Count);
        foreach (var (sent, read) in scene.Instances.Zip(decoded.Instances))
        {
            Assert.Equal(sent, read with { Xform = sent.Xform, Path = sent.Path });
            Assert.Equal(sent.Xform, read.Xform);
            Assert.Equal(sent.Path, read.Path);
        }
    }

    [Fact]
    public void SceneInstanceTableRejectsBrokenPlacements()
    {
        var scene = SceneFixture.Create() with
        {
            Instances = [new() { InstanceId = Guid.NewGuid(), DefinitionId = Guid.NewGuid(), DefinitionName = "Tree", Xform = Identity, Scale = new(1, 1, 1) }]
        };
        Assert.Empty(SceneValidator.Validate(scene));
        Assert.NotEmpty(SceneValidator.Validate(scene with { Instances = [scene.Instances[0] with { Path = [Guid.NewGuid()] }] }));
        Assert.NotEmpty(SceneValidator.Validate(scene with { Instances = [scene.Instances[0] with { Xform = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0] }] }));
        Assert.NotEmpty(SceneValidator.Validate(scene with { Instances = [scene.Instances[0] with { Xform = scene.Instances[0].Xform.Select((value, index) => index == 3 ? double.NaN : value).ToArray() }] }));
        Assert.NotEmpty(SceneValidator.Validate(scene with { Instances = [scene.Instances[0] with { DefinitionName = " " }] }));
        Assert.NotEmpty(SceneValidator.Validate(scene with { Instances = [scene.Instances[0], scene.Instances[0]] }));
        Assert.NotEmpty(SceneValidator.Validate(scene with { Instances = [scene.Instances[0] with { Scale = new(-1, 1, 1) }] }));
    }

    [Fact]
    public void MissingInstanceTableOnTheWireStaysReadable()
    {
        var scene = SceneFixture.Create();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(scene with { Instances = null! }, WireJson.Options);
        Assert.DoesNotContain("instances", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain(SceneValidator.Validate(SubmissionValidator.Deserialize<SceneManifest>(bytes)), issue => issue.Field == "instances");
    }

    private static readonly double[] Identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    [Fact]
    public void SavedViewComparisonAllowsNumericalRoundingButRejectsViewChanges()
    {
        var camera = SceneFixture.Create().Camera;
        Assert.True(CameraComparison.SameView(camera, camera with { Frustum = camera.Frustum with { Left = camera.Frustum.Left + 3e-16 } }));
        Assert.False(CameraComparison.SameView(camera, camera with { Frustum = camera.Frustum with { Left = camera.Frustum.Left + 1e-4 } }));
        Assert.False(CameraComparison.SameView(camera, camera with { Position = new(0.01, 0, 10) }));
        Assert.False(CameraComparison.SameView(camera, camera with { Projection = ProjectionKind.TwoPointPerspective }));
        Assert.False(CameraComparison.SameView(camera, camera with { ViewportPixels = new(4, 2) }));
    }

    [Fact]
    public void TwoPointCameraKeepsItsOffCentreFrustumThroughWireAndPromptContext()
    {
        var source = SceneFixture.Create();
        var scene = source with
        {
            Camera = source.Camera with
            {
                Projection = ProjectionKind.TwoPointPerspective,
                Direction = new(1, 0, 0),
                Up = new(0, 0, 1),
                Frustum = new(-0.8, 0.8, -0.2, 0.6, 1, 300)
            }
        };
        var decoded = SubmissionValidator.Deserialize<SceneManifest>(JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options));
        Assert.Empty(SceneValidator.Validate(decoded));
        Assert.Equal(scene.Camera, decoded.Camera);
        Assert.Equal(ProjectionKind.TwoPointPerspective, decoded.Camera.Projection);
        Assert.NotEqual(-decoded.Camera.Frustum.Bottom, decoded.Camera.Frustum.Top);
        var intent = ScenePromptComposer.Compose(decoded,
            new() { Id = "test", Name = "Test", BaseInstruction = "Keep the visible composition." },
            "", SceneFixture.Plan(decoded.Frame), includeNames: false);
        using var context = JsonDocument.Parse(intent.EffectivePrompt[(intent.EffectivePrompt.LastIndexOf('\n') + 1)..]);
        var camera = context.RootElement.GetProperty("camera");
        Assert.Equal("TwoPointPerspective", camera.GetProperty("projection").GetString());
        Assert.Equal(-0.2, camera.GetProperty("Frustum").GetProperty("Bottom").GetDouble());
        Assert.Equal(0.6, camera.GetProperty("Frustum").GetProperty("Top").GetDouble());
    }

    [Fact]
    public void CompatibleMinorVersionIgnoresOptionalExtensions()
    {
        var scene = SceneFixture.Create() with
        {
            SchemaVersion = "1.1"
        };
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(scene, WireJson.Options))!;
        json["vendorOptionalField"] = "unknown optional data";
        var decoded = SubmissionValidator.Deserialize<SceneManifest>(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Empty(SceneValidator.Validate(decoded));
        Assert.NotEmpty(SceneValidator.Validate(decoded with { RequiredFeatures = ["newBehavior"] }));
    }

    [Fact]
    public void DuplicateJsonPropertiesAreRejected()
    {
        Assert.Throws<JsonException>(() => SubmissionValidator.Deserialize<SceneManifest>("{\"sceneId\":1,\"sceneId\":2}"u8.ToArray()));
    }

    [Fact]
    public void PromptAlwaysIncludesPreservationAndKeepsOriginal()
    {
        var preset = new RepresentationPreset
        {
            Id = "custom",
            Name = "Custom",
            BaseInstruction = "Draw softly."
        };
        var prompt = PromptComposer.Compose(preset, "Add a stair", ["white plaster", new string('x', 2000)]);
        Assert.Equal("Add a stair", prompt.OriginalUserPrompt);
        Assert.Contains(PreservationRules.Objective, prompt.EffectivePrompt);
        Assert.Contains("conflicts with", prompt.EffectivePrompt);
        Assert.Contains(prompt.Warnings, w => w.Code == "contextTruncated");
    }

    [Fact]
    public void MappingUsesIdentityAndSurfacesPeerConflicts()
    {
        var id = Guid.NewGuid();
        var resolved = MaterialMappingResolver.Resolve([new(id, MappingSource.Inferred, "stone"), new(id, MappingSource.Material, "plaster")]);
        Assert.Equal("plaster", resolved.Description);
        Assert.True(MaterialMappingResolver.Resolve([new(id, MappingSource.Object, "brick"), new(id, MappingSource.Object, "wood")]).HasConflict);
    }

    [Theory]
    [InlineData("http://office.example/")]
    [InlineData("https://127.0.0.1/another")]
    [InlineData("http://localhost:5179/")]
    [InlineData("http://127.0.0.1/?token=secret")]
    [InlineData("http://user:secret@127.0.0.1/")]
    public void NetworkPolicyRejectsUnapprovedOrigins(string address) => Assert.Throws<ArgumentException>(() => EndpointPolicy.RequireLocalEndpoint(new(address)));
    [Fact]
    public void RedirectAndAbsoluteOutputUrlsAreRejected()
    {
        Assert.Throws<ProviderException>(() => EndpointPolicy.RequireOutputPath("https://example.org/image.png", Guid.NewGuid(), "reference"));
    }

    [Fact]
    public void CloudDescriptorsStateWhatIsWired()
    {
        // The wired clouds (ADR 0024–0027, klein profile completing BFL) are
        // selectable with their own key and opt-in, exactly like OpenAI.
        Assert.All(ProviderCatalogue.All, x => Assert.True(x.Available));
        Assert.DoesNotContain("scaffolded", ProviderCatalogue.All.Single(x => x.Id == "openai").Status, StringComparison.Ordinal);
        Assert.Equal("Requires an API key and cloud opt-in", ProviderCatalogue.All.Single(x => x.Id == "bfl").Status);
    }
}

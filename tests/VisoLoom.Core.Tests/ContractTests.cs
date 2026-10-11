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
    private static ResolutionConstraints Limits => new()
    {
        IsKnown = true,
        MinEdge = 1,
        MaxEdge = 8192,
        MinPixels = 1,
        MaxPixels = 16_777_216
    };

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
    public void CloudDescriptorsCannotExecute() => Assert.All(ProviderCatalogue.All.Where(x => x.Id != "local"), x => Assert.False(x.Available));
}

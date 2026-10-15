using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;
using VisoLoom.Providers;
using VisoLoom.Tests;
using Xunit;

namespace VisoLoom.Core.Tests;

public sealed class ComparisonSnapshotTests
{
    [Fact]
    public void ComparisonUsesOriginalFrameRatherThanThePaddedModelCanvas()
    {
        var scene = SceneFixture.Create();
        var output = Output(scene);
        var snapshot = ComparisonSnapshot.Create(scene, SceneFixture.Png, output);
        Assert.Equal(new PixelSize(2, 1), snapshot.Frame);
        Assert.Equal(new PixelSize(4, 2), snapshot.ModelCanvas);
        Assert.Equal(scene.SceneId, snapshot.SceneId);
        Assert.Equal(output.Metadata.ResultId, snapshot.ResultId);
        Assert.Equal(scene.CapturedAt, snapshot.CapturedAt);
    }

    [Fact]
    public void SourceAndResultRemainFrozenWhenInputBuffersAndSceneChange()
    {
        var scene = SceneFixture.Create();
        byte[] source = SceneFixture.Png;
        var output = Output(scene);
        var snapshot = ComparisonSnapshot.Create(scene, source, output);
        source[0] = output.PngBytes[0] = 0;
        var movedScene = scene with { SceneId = Guid.NewGuid(), Frame = new(4, 2) };
        using var sourceStream = snapshot.OpenSource();
        using var resultStream = snapshot.OpenResult();
        Assert.Equal(137, sourceStream.ReadByte());
        Assert.Equal(137, resultStream.ReadByte());
        Assert.False(sourceStream.CanWrite);
        Assert.Throws<NotSupportedException>(() => sourceStream.WriteByte(0));
        Assert.Equal(scene.SceneId, snapshot.SceneId);
        Assert.NotEqual(movedScene.SceneId, snapshot.SceneId);
        Assert.Equal(new PixelSize(2, 1), snapshot.Frame);
    }

    [Fact]
    public void ResultFromAnotherCaptureIsRejectedEvenAtMatchingDimensions()
    {
        var scene = SceneFixture.Create();
        var output = Output(scene);
        Assert.Throws<ArgumentException>(() => ComparisonSnapshot.Create(scene, SceneFixture.Png,
            output with { Metadata = output.Metadata with { SceneId = Guid.NewGuid() } }));
        Assert.Throws<ArgumentException>(() => ComparisonSnapshot.Create(scene with { CapturedAt = scene.CapturedAt.AddSeconds(1) }, SceneFixture.Png, output));
    }

    [Fact]
    public void ChangedImageBytesAndDifferentOutputFramesCannotBeComparedAsAligned()
    {
        var scene = SceneFixture.Create();
        var output = Output(scene);
        byte[] changed = SceneFixture.Png;
        changed[^1] ^= 1;
        Assert.Throws<ArgumentException>(() => ComparisonSnapshot.Create(scene, changed, output));
        Assert.Throws<ArgumentException>(() => ComparisonSnapshot.Create(scene, SceneFixture.Png, output with { PngBytes = changed }));
        Assert.Throws<ArgumentException>(() => ComparisonSnapshot.Create(scene, SceneFixture.Png,
            output with { Metadata = output.Metadata with { Plan = output.Metadata.Plan with { Final = new(4, 2) } } }));
    }

    private static ProviderResult Output(SceneManifest scene)
    {
        byte[] png = SceneFixture.Png;
        return new(png, new()
        {
            ResultId = Guid.NewGuid(), SceneId = scene.SceneId, ProviderId = "fixture", ModelId = "fixture", ModelVersion = "1",
            SceneDigest = ContentDigest.Sha256(JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options)),
            CompletedAt = DateTimeOffset.UtcNow, OutputSha256 = ContentDigest.Sha256(png), OutputByteCount = png.LongLength,
            Plan = new() { Capture = scene.Frame, Final = scene.Frame, Native = new(4, 2), ContentRectangle = new(0, 0, 4, 2), Scale = 2 }
        });
    }
}

using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;
using Xunit;

namespace VisoLoom.Core.Tests;

// ADR 0030's frame math: the largest centered rectangle at the freely chosen
// ratio inside the view, and the plan that carries it at one uniform scale.
// The locked path keeps ADR 0023's tests in ContractTests unchanged.
public sealed class SafeFrameTests
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
    [InlineData(1920, 1080, 1024, 768, 240, 0, 1440, 1080)]    // 4:3 in a 16:9 view
    [InlineData(480, 1200, 1920, 1080, 0, 465, 480, 270)]      // landscape in a narrow view
    [InlineData(1500, 1000, 1024, 1024, 250, 0, 1000, 1000)]   // square in a named view's size
    [InlineData(960, 540, 1024, 768, 120, 0, 720, 540)]        // half the view, the same ratio
    [InlineData(1080, 1920, 1080, 1920, 0, 0, 1080, 1920)]     // matching aspect fills the view
    public void FrameIsTheLargestCenteredRectangleAtTheChosenRatio(
        int viewWidth, int viewHeight, int width, int height,
        double expectedX, double expectedY, double expectedWidth, double expectedHeight)
    {
        var frame = SafeFrame.LargestCentered(new(viewWidth, viewHeight), new(width, height));
        Assert.Equal(expectedX, frame.X, 9);
        Assert.Equal(expectedY, frame.Y, 9);
        Assert.Equal(expectedWidth, frame.Width, 9);
        Assert.Equal(expectedHeight, frame.Height, 9);
        Assert.Equal((double)width / height, frame.Width / frame.Height, 9);
        Assert.InRange(frame.X, 0, viewWidth);
        Assert.InRange(frame.Y, 0, viewHeight);
        Assert.InRange(frame.X + frame.Width, 0, viewWidth + 1e-9);
        Assert.InRange(frame.Y + frame.Height, 0, viewHeight + 1e-9);
    }

    [Fact]
    public void RatiosTooExtremeForTheViewAreRefusedNotClamped()
    {
        Assert.Throws<ArgumentException>(() => SafeFrame.LargestCentered(new(1920, 1080), new(16000, 2)));
        Assert.Throws<ArgumentException>(() => SafeFrame.LargestCentered(new(1080, 1920), new(2, 16000)));
        Assert.Throws<ArgumentException>(() => SafeFrame.LargestCentered(new(1920, 1080), new(20000, 2000)));
    }

    [Fact]
    public void FreeFramePlanCarriesTheFrameAtOneUniformScaleWithoutNewFields()
    {
        var view = new PixelSize(1920, 1080);
        var final = new PixelSize(1024, 768);
        var plan = ResolutionPlanner.PlanFreeFrame(view, final, Limits);
        Assert.Equal(view, plan.Capture);
        Assert.Equal(final, plan.Final);
        Assert.True(ResolutionPlanner.Supports(plan.Native, Limits));
        Assert.NotNull(plan.Sampling);
        var frame = SafeFrame.LargestCentered(view, final);
        var content = plan.Sampling!.Value;
        Assert.Equal(frame.Width / frame.Height, content.Width / content.Height, 9);
        Assert.Equal(plan.Scale, content.Width / frame.Width, 9);
        Assert.Equal(plan.Scale, content.Height / frame.Height, 0);
        Assert.InRange(content.X, 0, plan.Native.Width);
        Assert.InRange(content.Y, 0, plan.Native.Height);
        // The frame's corner maps to the content bounds' corner, both ways.
        var canvasCorner = plan.ToNative(0, 0);
        Assert.Equal(content.X, canvasCorner.X, 9);
        Assert.Equal(content.Y, canvasCorner.Y, 9);
        var frameCorner = plan.ToCapture(content.X, content.Y);
        Assert.Equal(0, frameCorner.X, 9);
        Assert.Equal(0, frameCorner.Y, 9);
        // Interior frame points survive the round trip; the excluded edges stay outside.
        foreach (var point in new[] { (123.5, 45.0), (frame.Width / 2, frame.Height / 2), (frame.Width - 1, frame.Height - 1) })
        {
            var native = plan.ToNative(point.Item1, point.Item2);
            var restored = plan.ToCapture(native.X, native.Y);
            Assert.Equal(point.Item1, restored.X, 0);
            Assert.Equal(point.Item2, restored.Y, 0);
        }
        // One record, recomputable: the server can verify a framed request from its own scene.
        Assert.Equal(plan, JsonSerializer.Deserialize<ResolutionPlan>(JsonSerializer.Serialize(plan)));
        Assert.Equal(plan, ResolutionPlanner.PlanFreeFrame(view, final, Limits));
    }

    [Fact]
    public void FreeFramesFollowTheModelCanvasRules()
    {
        var square = Limits with { FixedSizes = [new(1024, 1024)] };
        var plan = ResolutionPlanner.PlanFreeFrame(new(1920, 1080), new(1024, 1024), square);
        Assert.Equal(new PixelSize(1024, 1024), plan.Native);
        Assert.Equal(0, plan.Sampling!.Value.X, 6);
        Assert.Equal(0, plan.Sampling!.Value.Y, 6);
        Assert.Equal(1024, plan.Sampling!.Value.Width, 6);
        Assert.Equal(1024, plan.Sampling!.Value.Height, 6);
        Assert.False(plan.RequiresResampling);
        var klein = ManagedModels.KleinFourBNative.Create("fixture").Resolution;
        var native = ResolutionPlanner.PlanFreeFrame(new(4096, 2304), new(512, 448), klein).Native;
        Assert.Equal(0, native.Width % klein.DimensionMultiple);
        Assert.Equal(0, native.Height % klein.DimensionMultiple);
        Assert.True(ResolutionPlanner.Supports(native, klein));
    }

    [Fact]
    public void UnsupportedModelCanvasesFailClosedWithAnHonestMessage()
    {
        var beyondTheEdge = Limits with { FixedSizes = [new(9000, 9000)] };
        Assert.Throws<ArgumentException>(
            () => ResolutionPlanner.PlanFreeFrame(new(1920, 1080), new(1024, 768), beyondTheEdge));
    }

    [Fact]
    public void TheLinkedPathStillRejectsUnrelatedAspects()
    {
        Assert.Throws<ArgumentException>(() => ResolutionPlanner.Plan(new(1920, 1080), new(1024, 768), Limits));
        var linked = ResolutionPlanner.Plan(new(1920, 1080), new(1024, 576), Limits);
        Assert.Equal(new PixelSize(1024, 576), linked.Final);
    }
}

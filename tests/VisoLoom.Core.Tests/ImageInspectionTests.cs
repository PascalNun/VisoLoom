using VisoLoom.Core;
using Xunit;

namespace VisoLoom.Core.Tests;

public sealed class ImageInspectionTests
{
    [Fact]
    public void FitPreservesAspectAndKeepsCompleteImageVisible()
    {
        var view = new ImageInspection(new(2000, 1000));
        view.SetViewport(600, 600, 2);
        Assert.Equal((0d, 150d, 600d, 300d), view.Bounds);
        Assert.Equal(60, view.ZoomPercent);
        Assert.Equal((0d, 0d), view.ToImage(0, 150));
        Assert.Equal((2000d, 1000d), view.ToImage(600, 450));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void ActualSizeUsesDisplayPixelsOnHighDpiScreens(double displayScale)
    {
        var view = new ImageInspection(new(2000, 1000));
        view.SetViewport(600, 400, displayScale);
        view.ActualSize();
        Assert.Equal(100, view.ZoomPercent, 8);
        Assert.Equal(1, view.Scale * displayScale, 8);
        Assert.False(view.IsFit);
    }

    [Fact]
    public void TogglingNeverChangesTransformOrInspectedImagePosition()
    {
        var view = new ImageInspection(new(2000, 1000));
        view.SetViewport(600, 400, 2);
        view.ActualSize();
        view.Pan(-70, -30);
        var bounds = view.Bounds;
        var pixel = view.ToImage(220, 160);
        for (int i = 0; i < 10; i++)
        {
            view.Toggle();
            Assert.Equal(i % 2 == 1, view.ShowResult);
            Assert.Equal(bounds, view.Bounds);
            Assert.Equal(pixel, view.ToImage(220, 160));
        }
    }

    [Fact]
    public void ZoomKeepsThePointerOverTheSameImagePoint()
    {
        var view = new ImageInspection(new(2000, 1200));
        view.SetViewport(600, 400);
        view.ActualSize();
        var point = view.ToImage(280, 170);
        view.Zoom(1.5, 280, 170);
        Assert.Equal(point.X, view.ToImage(280, 170).X, 8);
        Assert.Equal(point.Y, view.ToImage(280, 170).Y, 8);
    }

    [Fact]
    public void PanningCannotLoseTheImageOutsideTheWindow()
    {
        var view = new ImageInspection(new(2000, 1000));
        view.SetViewport(600, 400);
        view.ActualSize();
        view.Pan(1e9, 1e9);
        Assert.Equal((0d, 0d, 2000d, 1000d), view.Bounds);
        view.Pan(-1e9, -1e9);
        Assert.Equal((-1400d, -600d, 2000d, 1000d), view.Bounds);
        view.Fit();
        view.Pan(100, 100);
        Assert.Equal((0d, 50d, 600d, 300d), view.Bounds);
    }

    [Fact]
    public void ResizeAndScreenChangePreservePhysicalZoomAndImageCentre()
    {
        var view = new ImageInspection(new(8000, 2000));
        view.SetViewport(600, 400, 2);
        view.ActualSize();
        view.Pan(-150, -30);
        var centre = view.ToImage(300, 200);
        view.SetViewport(900, 500, 1.5);
        Assert.Equal(100, view.ZoomPercent, 8);
        Assert.Equal(centre.X, view.ToImage(450, 250).X, 8);
        Assert.Equal(centre.Y, view.ToImage(450, 250).Y, 8);
    }

    [Fact]
    public void FitTracksResizeAndDoesNotEnlargeSmallImages()
    {
        var view = new ImageInspection(new(100, 50));
        view.SetViewport(800, 500, 2);
        Assert.Equal(100, view.ZoomPercent);
        Assert.Equal((375d, 237.5d, 50d, 25d), view.Bounds);
        view.SetViewport(40, 40, 2);
        Assert.Equal((0d, 10d, 40d, 20d), view.Bounds);
        Assert.True(view.IsFit);
    }

    [Fact]
    public void InvalidTransformsAreRejectedAndZoomIsBounded()
    {
        var view = new ImageInspection(new(2000, 1000));
        view.SetViewport(600, 400, 2);
        Assert.Throws<ArgumentException>(() => view.SetViewport(0, 100));
        Assert.Throws<ArgumentException>(() => view.SetViewport(100, 100, double.NaN));
        Assert.Throws<ArgumentException>(() => view.Pan(double.PositiveInfinity, 0));
        Assert.Throws<ArgumentException>(() => view.Zoom(-1, 0, 0));
        Assert.Throws<ArgumentException>(() => view.Zoom(1, double.NaN, 0));
        view.Zoom(1e100, 300, 200);
        Assert.Equal(800, view.ZoomPercent);
        view.Zoom(1e-100, 300, 200);
        Assert.True(view.ZoomPercent > 0);
    }
}

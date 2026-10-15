namespace VisoLoom.Core;

// One transform for both images. This controls inspection only; it never changes
// capture framing, saved pixels, model input or Rhino's camera.
public sealed class ImageInspection
{
    public PixelSize Image { get; }
    public bool ShowResult { get; private set; } = true;
    public bool IsFit { get; private set; } = true;
    public double Scale { get; private set; } = 1;
    public double DisplayScale { get; private set; } = 1;
    private double width = 1, height = 1, x, y;
    public double ZoomPercent => 100 * Scale * DisplayScale;
    public (double X, double Y, double Width, double Height) Bounds => (x, y, Image.Width * Scale, Image.Height * Scale);
    private double FitScale => Math.Min(1 / DisplayScale, Math.Min(width / Image.Width, height / Image.Height));

    public ImageInspection(PixelSize image)
    {
        ResolutionPlanner.ValidateSize(image);
        Image = image;
    }

    public void SetViewport(double newWidth, double newHeight, double displayScale = 1)
    {
        if (!Positive(newWidth) || !Positive(newHeight) || !Positive(displayScale))
            throw new ArgumentException("Inspection needs a finite viewport and display scale.");
        var centre = ToImage(width / 2, height / 2);
        double physicalScale = Scale * DisplayScale;
        width = newWidth; height = newHeight; DisplayScale = displayScale;
        if (IsFit) Fit();
        else
        {
            Scale = physicalScale / DisplayScale;
            x = width / 2 - centre.X * Scale;
            y = height / 2 - centre.Y * Scale;
            Clamp();
        }
    }

    public void Fit()
    {
        IsFit = true;
        Scale = FitScale;
        x = (width - Image.Width * Scale) / 2;
        y = (height - Image.Height * Scale) / 2;
    }

    public void ActualSize() => SetScale(1 / DisplayScale, width / 2, height / 2);
    public void Toggle() => ShowResult = !ShowResult;
    public void Zoom(double factor, double anchorX, double anchorY)
    {
        if (!Positive(factor) || !double.IsFinite(anchorX) || !double.IsFinite(anchorY))
            throw new ArgumentException("Inspection zoom must be finite and positive.");
        SetScale(Math.Clamp(Scale * factor, FitScale / 16, 8 / DisplayScale), anchorX, anchorY);
    }
    public void Pan(double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) throw new ArgumentException("Inspection movement must be finite.");
        IsFit = false;
        x += dx; y += dy;
        Clamp();
    }

    public (double X, double Y) ToImage(double screenX, double screenY) => ((screenX - x) / Scale, (screenY - y) / Scale);
    public (double X, double Y) ToView(double imageX, double imageY) => (imageX * Scale + x, imageY * Scale + y);

    private void SetScale(double scale, double anchorX, double anchorY)
    {
        var anchor = ToImage(anchorX, anchorY);
        IsFit = false;
        Scale = scale;
        x = anchorX - anchor.X * Scale;
        y = anchorY - anchor.Y * Scale;
        Clamp();
    }
    private void Clamp()
    {
        double w = Image.Width * Scale, h = Image.Height * Scale;
        x = w <= width ? (width - w) / 2 : Math.Clamp(x, width - w, 0);
        y = h <= height ? (height - h) / 2 : Math.Clamp(y, height - h, 0);
    }
    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
}

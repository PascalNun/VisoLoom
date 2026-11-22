using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using VisoLoom.Core;
using VisoLoom.Protocol;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

// Uses Rhino's drawing runtime, never a standalone drawing dependency.
internal sealed class RhinoImageProcessor : IImageProcessor
{
    public byte[] PrepareReference(byte[] png, ResolutionPlan plan)
    {
        if (plan.IsExact)
            return png;
        // The linked path maps the complete capture; a lock-off plan maps only
        // its safe frame (ADR 0030) — the same rectangle the conduit shows,
        // recomputed from the record's own sizes.
        var source = ResolutionPlanner.KeepsLinkedAspect(plan.Capture, plan.Final)
            ? new RectangleF(0, 0, plan.Capture.Width, plan.Capture.Height)
            : Frame(plan);
        return Transform(png, plan.Capture, plan.Native, source, Bounds(plan), plan.Sampling is not null);
    }

    private static RectangleF Frame(ResolutionPlan plan)
    {
        var frame = SafeFrame.LargestCentered(plan.Capture, plan.Final);
        return new((float)frame.X, (float)frame.Y, (float)frame.Width, (float)frame.Height);
    }

    public byte[] NormalizeResult(byte[] image, ResolutionPlan plan)
    {
        // Cloud results arrive in the provider's documented mime — xAI's edit
        // surface serves JPEG unless the served quality produces PNG — so the
        // host codec decides what was returned; anything it cannot decode
        // fails closed at the caller. The restored frame is always re-encoded
        // as PNG, the plug-in's export format.
        bool png = IsPng(image);
        if (plan.IsExact)
        {
            using var bitmap = Decode(image);
            // A provider may answer at its own pixel count when the aspect matches the
            // plan's frame: the same drawing rescaled restores honestly, a different
            // framing does not.
            if (bitmap.Width == plan.Native.Width && bitmap.Height == plan.Native.Height)
            {
                _ = bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1);
                return png ? image : EncodePng(bitmap);
            }
            if (ResolutionPlanner.ProviderCanvasScale(plan.Native, new(bitmap.Width, bitmap.Height)) is null)
                throw new ArgumentException("The host decoded unexpected image dimensions.");
            return Rescale(bitmap, plan.Native);
        }
        // A framed result fills the requested dimensions exactly; a linked
        // result letterboxes only the whole-pixel rounding ADR 0023 allows.
        var fitted = ResolutionPlanner.FitCanvas(plan.Capture, plan.Final);
        var destination = ResolutionPlanner.KeepsLinkedAspect(plan.Capture, plan.Final)
            ? new RectangleF((float)fitted.X, (float)fitted.Y, (float)fitted.Width, (float)fitted.Height)
            : new(0, 0, plan.Final.Width, plan.Final.Height);
        return Restore(image, plan.Native, plan.Final, Bounds(plan), destination,
            plan.Sampling is not null || plan.Capture != plan.Final);
    }

    // A PNG header alone is insufficient to decode cloud output, but it still
    // tells the passthrough path from a re-encode neatly.
    private static bool IsPng(byte[] image)
    {
        try { PngHeader.Read(image); return true; }
        catch (Exception ex) when (ex is ArgumentException or FormatException) { return false; }
    }

    private static Bitmap Decode(byte[] image)
    {
        using var stream = new MemoryStream(image);
        return new Bitmap(stream);
    }

    // The result twin of Transform: the source is the provider's returned
    // bytes in any codec it documented, the dimension check happens on the
    // decoded bitmap, and the output is always a PNG export.
    private static byte[] Restore(byte[] image, PixelSize expected, PixelSize output, RectangleF source, RectangleF destination, bool fractional)
    {
        using var input = Decode(image);
        if (input.Width != expected.Width || input.Height != expected.Height)
        {
            // The provider answered at its own pixel count; accept it only as the same
            // drawing rescaled — the content rectangle rides the uniform rescale, so
            // no pixel is reframed on its way back into the Rhino frame.
            var uniform = ResolutionPlanner.ProviderCanvasScale(expected, new(input.Width, input.Height))
                ?? throw new ArgumentException("Unexpected image dimensions before processing.");
            source = new((float)(source.X * uniform.X), (float)(source.Y * uniform.Y),
                (float)(source.Width * uniform.X), (float)(source.Height * uniform.Y));
            fractional = true;
        }
        return Draw(input, output, source, destination, fractional);
    }

    private static byte[] Rescale(Bitmap input, PixelSize output) =>
        Draw(input, output, new(0, 0, input.Width, input.Height), new(0, 0, output.Width, output.Height), true);

    private static byte[] Draw(Bitmap input, PixelSize output, RectangleF source, RectangleF destination, bool fractional)
    {
        using var bitmap = new Bitmap(output.Width, output.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // Rhino for Mac does not implement ImageAttributes.SetWrapMode.
            if (fractional)
                graphics.DrawImage(input, destination, source, GraphicsUnit.Pixel);
            else
                graphics.DrawImage(input, new Rectangle((int)destination.X, (int)destination.Y, (int)destination.Width, (int)destination.Height),
                    (int)source.X, (int)source.Y, (int)source.Width, (int)source.Height, GraphicsUnit.Pixel);
        }
        return EncodePng(bitmap);
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var encoded = new MemoryStream();
        bitmap.Save(encoded, ImageFormat.Png);
        return encoded.ToArray();
    }

    private static RectangleF Bounds(ResolutionPlan plan) => plan.Sampling is { } sample
        ? new((float)sample.X, (float)sample.Y, (float)sample.Width, (float)sample.Height)
        : new(plan.ContentRectangle.X, plan.ContentRectangle.Y, plan.ContentRectangle.Width, plan.ContentRectangle.Height);

    private static byte[] Transform(byte[] png, PixelSize expected, PixelSize output, RectangleF source, RectangleF destination, bool fractional)
    {
        if (PngHeader.Read(png) != expected)
            throw new ArgumentException("Unexpected image dimensions before processing.");
        using var stream = new MemoryStream(png);
        using var input = new Bitmap(stream);
        if (input.Width != expected.Width || input.Height != expected.Height)
            throw new ArgumentException("The host decoded unexpected image dimensions.");
        using var bitmap = new Bitmap(output.Width, output.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // Rhino for Mac does not implement ImageAttributes.SetWrapMode.
            if (fractional)
                graphics.DrawImage(input, destination, source, GraphicsUnit.Pixel);
            else
                graphics.DrawImage(input, new Rectangle((int)destination.X, (int)destination.Y, (int)destination.Width, (int)destination.Height),
                    (int)source.X, (int)source.Y, (int)source.Width, (int)source.Height, GraphicsUnit.Pixel);
        }
        using var encoded = new MemoryStream();
        bitmap.Save(encoded, ImageFormat.Png);
        return encoded.ToArray();
    }
}

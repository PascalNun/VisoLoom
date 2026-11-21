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
        return Transform(png, plan.Capture, plan.Native,
            new(0, 0, plan.Capture.Width, plan.Capture.Height), Bounds(plan), plan.Sampling is not null);
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
            if (bitmap.Width != plan.Native.Width || bitmap.Height != plan.Native.Height)
                throw new ArgumentException("The host decoded unexpected image dimensions.");
            _ = bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1);
            return png ? image : EncodePng(bitmap);
        }
        var fitted = ResolutionPlanner.FitCanvas(plan.Capture, plan.Final);
        return Restore(image, plan.Native, plan.Final, Bounds(plan),
            new((float)fitted.X, (float)fitted.Y, (float)fitted.Width, (float)fitted.Height),
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
            throw new ArgumentException("Unexpected image dimensions before processing.");
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

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

    public byte[] NormalizeResult(byte[] png, ResolutionPlan plan)
    {
        if (plan.IsExact)
        {
            // Decode cloud output even when no resize is needed; a PNG header alone is insufficient.
            using var stream = new MemoryStream(png);
            using var image = new Bitmap(stream);
            if (image.Width != plan.Native.Width || image.Height != plan.Native.Height)
                throw new ArgumentException("The host decoded unexpected image dimensions.");
            _ = image.GetPixel(image.Width - 1, image.Height - 1);
            return png;
        }
        var fitted = ResolutionPlanner.FitCanvas(plan.Capture, plan.Final);
        return Transform(png, plan.Native, plan.Final, Bounds(plan),
            new((float)fitted.X, (float)fitted.Y, (float)fitted.Width, (float)fitted.Height),
            plan.Sampling is not null || plan.Capture != plan.Final);
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

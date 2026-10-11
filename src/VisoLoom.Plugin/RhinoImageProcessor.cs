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
            new(0, 0, plan.Capture.Width, plan.Capture.Height), plan.ContentRectangle);
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
        return Transform(png, plan.Native, plan.Final, plan.ContentRectangle,
            new(0, 0, plan.Final.Width, plan.Final.Height));
    }

    private static byte[] Transform(byte[] png, PixelSize expected, PixelSize output, PixelRectangle source, PixelRectangle destination)
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
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(input, new Rectangle(destination.X, destination.Y, destination.Width, destination.Height),
                source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }
        using var encoded = new MemoryStream();
        bitmap.Save(encoded, ImageFormat.Png);
        return encoded.ToArray();
    }
}

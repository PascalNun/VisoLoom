namespace VisoLoom.Core;

public static class ResolutionPlanner
{
    public const long CapturePixelLimit = 16_777_216;
    public const long ExportPixelLimit = 67_108_864;

    // Export dimensions need not lie on the camera's reduced-ratio grid. Fit the
    // full image uniformly into the rounded canvas; never alter the Rhino camera.
    public static PixelSize OutputWidth(PixelSize viewport, int width)
    {
        ValidateSize(viewport);
        if (width is < 1 or > 16384)
            throw new ArgumentOutOfRangeException(nameof(width), "Choose a width between 1 and 16,384 pixels.");
        var size = new PixelSize(width, Math.Max(1, checked((int)Math.Round((double)width * viewport.Height / viewport.Width))));
        ValidateSize(size, ExportPixelLimit);
        return size;
    }

    public static PixelSize OutputHeight(PixelSize viewport, int height)
    {
        var size = OutputWidth(new(viewport.Height, viewport.Width), height);
        return new(size.Height, size.Width);
    }

    public static PixelSize CaptureSize(PixelSize viewport, PixelSize output)
    {
        ValidateSize(viewport);
        ValidateSize(output, ExportPixelLimit);
        return output.Pixels <= CapturePixelLimit && (long)viewport.Width * output.Height == (long)viewport.Height * output.Width
            ? output : viewport;
    }

    public static ResolutionPlan ReferencePlan(PixelSize capture, PixelSize final)
    {
        ValidateSize(capture);
        ValidateSize(final, ExportPixelLimit);
        RequireLinkedAspect(capture, final);
        return new() { Capture = capture, Native = capture, Final = final,
            ContentRectangle = new(0, 0, capture.Width, capture.Height), Scale = 1 };
    }

    public static SamplingRectangle FitCanvas(PixelSize source, PixelSize canvas)
    {
        double scale = Math.Min((double)canvas.Width / source.Width, (double)canvas.Height / source.Height);
        double width = source.Width * scale, height = source.Height * scale;
        return new(Math.Max(0, (canvas.Width - width) / 2), Math.Max(0, (canvas.Height - height) / 2), width, height);
    }

    private static void RequireLinkedAspect(PixelSize capture, PixelSize final)
    {
        var fit = FitCanvas(capture, final);
        if (fit.X > 0.500000001 || fit.Y > 0.500000001)
            throw new ArgumentException("Keep the current view's proportions. Change the Rhino viewport to use a different frame.");
    }

    public static PixelSize ScaleViewport(PixelSize viewport, int multiplier)
    {
        if (multiplier is not (1 or 2 or 4))
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        var size = new PixelSize(checked(viewport.Width * multiplier), checked(viewport.Height * multiplier));
        ValidateSize(size, ExportPixelLimit);
        return size;
    }

    public static void ValidateSize(PixelSize size, long maximumPixels = CapturePixelLimit)
    {
        if (size.Width <= 0 || size.Height <= 0 || size.Width > 16384 || size.Height > 16384 || size.Pixels > maximumPixels)
            throw new ArgumentException($"Choose a size up to {maximumPixels / 1_048_576:N0} megapixels and 16,384 pixels per side.");
    }

    public static ResolutionPlan Plan(PixelSize capture, PixelSize final, ResolutionConstraints constraints)
    {
        ValidateSize(capture);
        ValidateSize(final, ExportPixelLimit);
        RequireLinkedAspect(capture, final);
        if (!constraints.IsKnown || constraints.DimensionMultiple <= 0 || constraints.MinEdge <= 0 ||
            constraints.MaxEdge < constraints.MinEdge || constraints.MinPixels <= 0 || constraints.MaxPixels < constraints.MinPixels ||
            constraints.MaximumAspectRatio is { } ratioLimit && (!double.IsFinite(ratioLimit) || ratioLimit < 1))
            throw new ArgumentException("The model does not declare usable image size limits.");
        List<PixelSize> candidates = [capture, final];
        if (constraints.FixedSizes.Count != 0)
            candidates.AddRange(constraints.FixedSizes);
        else
        {
            int multiple = constraints.DimensionMultiple;
            foreach (var size in new[] { capture, final }.Distinct())
            {
                int width = checked((Math.Max(size.Width, constraints.MinEdge) + multiple - 1) / multiple * multiple);
                int height = checked((Math.Max(size.Height, constraints.MinEdge) + multiple - 1) / multiple * multiple);
                candidates.Add(new(width, height));
            }
            // Search supported grid widths, choosing the largest proportional height within limits.
            // A bounded edge limit prevents capabilities from causing unbounded planning work.
            if (constraints.MaxEdge > 16384)
                throw new ArgumentException("The declared model edge limit is too large.");
            for (int w = multiple; w <= constraints.MaxEdge; w += multiple)
            {
                int h = (int)Math.Min(constraints.MaxEdge, constraints.MaxPixels / w) / multiple * multiple;
                if (h > 0)
                    candidates.Add(new(w, h));
                long requiredHeight = Math.Max(constraints.MinEdge, constraints.MinPixels / w + (constraints.MinPixels % w == 0 ? 0 : 1));
                if (requiredHeight > constraints.MaxEdge)
                    continue;
                int containingHeight = checked((int)((Math.Max(final.Height, requiredHeight) + multiple - 1) / multiple * multiple));
                candidates.Add(new(w, containingHeight));
                foreach (var ratio in constraints.AllowedRatios)
                {
                    if (ratio.Width <= 0 || ratio.Height <= 0)
                        throw new ArgumentException("Invalid model aspect ratio.");
                    long numerator = (long)w * ratio.Height;
                    if (numerator % ratio.Width == 0 && numerator / ratio.Width <= constraints.MaxEdge)
                        candidates.Add(new(w, (int)(numerator / ratio.Width)));
                }
            }
        }

        int gcd = Gcd(capture.Width, capture.Height);
        int rw = capture.Width / gcd, rh = capture.Height / gcd;
        double targetScale = Math.Min((double)final.Width / capture.Width, (double)final.Height / capture.Height);
        var plans = new List<ResolutionPlan>();
        foreach (var canvas in candidates.Distinct().Where(s => Supports(s, constraints)))
        {
            if (constraints.AllowFractionalSampling)
            {
                double scale = Math.Min((double)canvas.Width / capture.Width, (double)canvas.Height / capture.Height);
                scale = Math.Min(scale, targetScale);
                if (!constraints.AllowUpscaling) scale = Math.Min(scale, 1);
                double contentWidth = capture.Width * scale, contentHeight = capture.Height * scale;
                double x = (canvas.Width - contentWidth) / 2, y = (canvas.Height - contentHeight) / 2;
                int left = (int)Math.Floor(x), top = (int)Math.Floor(y);
                plans.Add(new() { Capture = capture, Final = final, Native = canvas, Scale = scale,
                    Sampling = new(x, y, contentWidth, contentHeight), ContentRectangle = new(left, top,
                        Math.Min(canvas.Width, (int)Math.Ceiling(x + contentWidth)) - left,
                        Math.Min(canvas.Height, (int)Math.Ceiling(y + contentHeight)) - top) });
                continue;
            }
            int units = Math.Min(canvas.Width / rw, canvas.Height / rh);
            if (!constraints.AllowUpscaling)
                units = Math.Min(units, gcd);
            if (units == 0)
                continue;
            int cw = rw * units, ch = rh * units;
            plans.Add(new()
            {
                Capture = capture,
                Final = final,
                Native = canvas,
                ContentRectangle = new((canvas.Width - cw) / 2, (canvas.Height - ch) / 2, cw, ch),
                Scale = (double)units / gcd
            });
        }

        return plans.OrderByDescending(p => Math.Min(p.Scale / targetScale, 1))
            .ThenBy(p => Math.Max(0, p.Scale - targetScale))
            .ThenBy(p => p.Native.Pixels - (long)p.ContentRectangle.Width * p.ContentRectangle.Height)
            .ThenBy(p => p.Native.Pixels).ThenBy(p => p.Native.Width).FirstOrDefault()
            ?? throw new ArgumentException("No supported resolution preserves the complete Rhino frame.");
    }

    public static bool Supports(PixelSize s, ResolutionConstraints c) =>
        c.IsKnown && c.DimensionMultiple > 0 &&
        s.Width >= c.MinEdge && s.Height >= c.MinEdge && s.Width <= c.MaxEdge && s.Height <= c.MaxEdge &&
        s.Pixels >= c.MinPixels && s.Pixels <= c.MaxPixels &&
        s.Width % c.DimensionMultiple == 0 && s.Height % c.DimensionMultiple == 0 &&
        (c.MaximumAspectRatio is null || double.IsFinite(c.MaximumAspectRatio.Value) && c.MaximumAspectRatio >= 1 &&
            Math.Max(s.Width, s.Height) <= Math.Min(s.Width, s.Height) * c.MaximumAspectRatio.Value) &&
        (c.FixedSizes.Count == 0 || c.FixedSizes.Contains(s)) &&
        (c.AllowedRatios.Count == 0 || c.AllowedRatios.Any(r => r.Width > 0 && r.Height > 0 &&
            (long)s.Width * r.Height == (long)s.Height * r.Width));
    private static int Gcd(int a, int b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }
}

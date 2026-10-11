namespace VisoLoom.Core;

public static class ResolutionPlanner
{
    public const long CapturePixelLimit = 16_777_216;
    public static PixelSize ScaleViewport(PixelSize viewport, int multiplier)
    {
        if (multiplier is not (1 or 2 or 4))
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        var size = new PixelSize(checked(viewport.Width * multiplier), checked(viewport.Height * multiplier));
        ValidateSize(size, CapturePixelLimit);
        return size;
    }

    public static void ValidateSize(PixelSize size, long maximumPixels = CapturePixelLimit)
    {
        if (size.Width <= 0 || size.Height <= 0 || size.Width > 16384 || size.Height > 16384 || size.Pixels > maximumPixels)
            throw new ArgumentException("The image dimensions exceed the supported capture limits.");
    }

    public static ResolutionPlan Plan(PixelSize capture, PixelSize final, ResolutionConstraints constraints)
    {
        ValidateSize(capture);
        ValidateSize(final);
        if ((long)capture.Width * final.Height != (long)capture.Height * final.Width)
            throw new ArgumentException("Custom resolution must preserve the current Rhino view's aspect ratio.");
        if (!constraints.IsKnown || constraints.DimensionMultiple <= 0 || constraints.MinEdge <= 0 ||
            constraints.MaxEdge < constraints.MinEdge || constraints.MinPixels <= 0 || constraints.MaxPixels < constraints.MinPixels ||
            constraints.MaximumAspectRatio is { } ratioLimit && (!double.IsFinite(ratioLimit) || ratioLimit < 1))
            throw new ArgumentException("The model does not declare usable image size limits.");
        List<PixelSize> candidates = [capture];
        if (constraints.FixedSizes.Count != 0)
            candidates.AddRange(constraints.FixedSizes);
        else
        {
            int multiple = constraints.DimensionMultiple;
            int width = checked((Math.Max(capture.Width, constraints.MinEdge) + multiple - 1) / multiple * multiple);
            int height = checked((Math.Max(capture.Height, constraints.MinEdge) + multiple - 1) / multiple * multiple);
            candidates.Add(new(width, height));
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
                int containingHeight = checked((int)((Math.Max(capture.Height, requiredHeight) + multiple - 1) / multiple * multiple));
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
        var plans = new List<ResolutionPlan>();
        foreach (var canvas in candidates.Distinct().Where(s => Supports(s, constraints)))
        {
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

        return plans.OrderByDescending(p => Math.Min(p.Scale, 1)).ThenBy(p => p.Native.Pixels - (long)p.ContentRectangle.Width * p.ContentRectangle.Height).ThenBy(p => p.Native.Pixels).ThenBy(p => p.Native.Width).FirstOrDefault() ?? throw new ArgumentException("No supported resolution preserves the complete Rhino frame.");
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

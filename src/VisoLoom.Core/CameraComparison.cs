namespace VisoLoom.Core;

public static class CameraComparison
{
    // Native redraw/capture can round equivalent frustum values by a few floating-point bits.
    // Projection and pixels remain exact; this tolerance is far below a visible camera edit.
    public static bool SameView(CameraMetadata a, CameraMetadata b) =>
        a.Projection == b.Projection && a.ViewportPixels == b.ViewportPixels &&
        Values(a).Zip(Values(b), (x, y) => Math.Abs(x - y) <= 1e-12 * Math.Max(1, Math.Max(Math.Abs(x), Math.Abs(y)))).All(equal => equal);

    private static double[] Values(CameraMetadata c) =>
    [
        c.Position.X, c.Position.Y, c.Position.Z, c.Direction.X, c.Direction.Y, c.Direction.Z,
        c.Up.X, c.Up.Y, c.Up.Z, c.Frustum.Left, c.Frustum.Right,
        c.Frustum.Bottom, c.Frustum.Top, c.Frustum.Near, c.Frustum.Far
    ];
}

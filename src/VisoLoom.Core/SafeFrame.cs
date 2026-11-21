namespace VisoLoom.Core;

// ADR 0030's centered safe frame: the largest centered rectangle at the freely
// chosen output ratio inside the selected view's device pixels. The view's
// device size arrives DPR-corrected from the viewport capture path; the same
// rectangle serves the on-screen overlay, the captured reference and the
// comparison, and it stays a strict subset of the visible view.
public static class SafeFrame
{
    // The frame for a lock-off session: the largest centered rectangle at the
    // final image's ratio within the view. A ratio so extreme that the frame
    // collapses below one device pixel is refused — never silently clamped.
    public static SamplingRectangle LargestCentered(PixelSize view, PixelSize final)
    {
        ResolutionPlanner.ValidateSize(view);
        ResolutionPlanner.ValidateSize(final, ResolutionPlanner.ExportPixelLimit);
        double width = Math.Min(view.Width, (double)view.Height * final.Width / final.Height);
        double height = Math.Min(view.Height, (double)view.Width * final.Height / final.Width);
        if (width < 1 || height < 1)
            throw new ArgumentException("The chosen proportions are too narrow for this view to frame. Keep the camera's composition and choose closer dimensions.");
        return new((view.Width - width) / 2, (view.Height - height) / 2, width, height);
    }
}

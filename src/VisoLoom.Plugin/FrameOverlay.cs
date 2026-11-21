using System.Drawing;
using Rhino.Display;
using VisoLoom.Core;

namespace VisoLoom.Plugin;

// ADR 0030's on-screen safe frame: a screen-space conduit that exists only
// while the panel holds a lock-off ratio, drawing the centered frame of the
// selected view with its excluded edges dimmed. The capture path suspends it
// around the capture call, so the overlay never reaches a saved source image.
internal sealed class FrameOverlay : DisplayConduit
{
    private Guid viewportId;
    private PixelSize chosen = new(1, 1);
    private bool active;
    private int suspended;

    // The panel's one honest state setter: a null size turns the frame off.
    // Announcing the identical state again changes nothing here — the conduit
    // draws at whatever device size the viewport has at draw time — and a
    // repeated identical write, each with its own redraw, made Rhino recompute
    // the camera frustum by rounding noise, expiring every fresh preview
    // within a second while a locked-off frame was shown.
    public bool Update(Guid viewport, PixelSize? ratio)
    {
        bool newActive = ratio is not null;
        PixelSize newChosen = ratio ?? new(1, 1);
        bool enabled = newActive && suspended == 0;
        if (active == newActive && chosen == newChosen && viewportId == viewport && Enabled == enabled)
            return false;
        active = newActive;
        chosen = newChosen;
        viewportId = viewport;
        Enabled = enabled;
        return true;
    }

    public void TurnOff() => Update(Guid.Empty, null);

    // The capture path disables the conduit around the capture call; a nested
    // suspension stays off until the last one resumes.
    public void Suspend()
    {
        suspended++;
        if (Enabled)
            Enabled = false;
    }

    public void Resume()
    {
        if (suspended > 0 && --suspended == 0 && active && !Enabled)
            Enabled = true;
    }

    public bool IsSuspended => suspended > 0;
    public bool IsShown => active && Enabled;

    protected override void DrawOverlay(DrawEventArgs e)
    {
        if (!active || e.Viewport.Id != viewportId)
            return;
        // The live device size at draw time keeps the frame tracking view
        // resizes without the panel re-announcing them.
        var view = e.Viewport.Size;
        SamplingRectangle frame;
        try { frame = SafeFrame.LargestCentered(new(view.Width, view.Height), chosen); }
        catch (ArgumentException) { return; }
        int left = (int)frame.X, top = (int)frame.Y, width = (int)frame.Width, height = (int)frame.Height;
        // Excluded edges are visibly dimmed; the frame itself is outlined.
        e.Display.Draw2dRectangle(new Rectangle(0, 0, view.Width, Math.Max(0, top)), Dim, 0, Dim);
        e.Display.Draw2dRectangle(new Rectangle(0, top + height, view.Width, view.Height - top - height), Dim, 0, Dim);
        e.Display.Draw2dRectangle(new Rectangle(0, top, Math.Max(0, left), height), Dim, 0, Dim);
        e.Display.Draw2dRectangle(new Rectangle(left + width, top, view.Width - left - width, height), Dim, 0, Dim);
        e.Display.Draw2dRectangle(new Rectangle(left, top, width, height), Color.Transparent, 2, Edge);
    }

    private static readonly Color Dim = Color.FromArgb(70, 0, 0, 0);
    private static readonly Color Edge = Color.FromArgb(200, 255, 255, 255);
}

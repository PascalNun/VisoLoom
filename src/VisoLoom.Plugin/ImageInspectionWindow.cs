using Eto.Drawing;
using Eto.Forms;
using VisoLoom.Core;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal sealed class ImageInspectionWindow : Dialog
{
    private readonly Drawable canvas = new() { CanFocus = true, Width = 1, Height = 1, BackgroundColor = SystemColors.ControlBackground };
    private readonly WrappingLabel showing = VisoLoomPanel.CreateLabel("", bold: true);
    private readonly Label zoom = new();
    private readonly Button toggle = new() { Text = "Show source" };
    private readonly Button wipeButton = new() { Text = "Wipe", ToolTip = "Inspect the source and result with a movable divider" };
    private readonly Slider divider = new() { MinValue = 0, MaxValue = 100, Value = 50, Width = 1, ToolTip = "Move the divider between source and result" };
    private bool wipe;
    private readonly ImageInspection view;
    private PointF? dragging;

    private ImageInspectionWindow(Bitmap source, Bitmap? result, string description, string processing, string singleImageLabel, bool startWithWipe = false)
    {
        Title = result is null ? "VisoLoom · " + singleImageLabel : "VisoLoom · Compare with Rhino capture";
        ClientSize = new Size(960, 720);
        MinimumSize = new Size(640, 400);
        Resizable = true;
        view = new(new(source.Width, source.Height));
        wipe = result is not null && startWithWipe;
        var fit = new Button { Text = "Fit" };
        var actual = new Button { Text = "100%" };
        var smaller = new Button { Text = "−" };
        var larger = new Button { Text = "+" };
        var close = new Button { Text = "Close" };
        var layout = new DynamicLayout { Padding = 12, Spacing = new Size(8, 8) };
        layout.AddRow(VisoLoomPanel.CreateLabel(description));
        layout.AddRow(VisoLoomPanel.CreateLabel(processing));
        toggle.Visible = wipeButton.Visible = result is not null;
        layout.AddRow(new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows = { new TableRow(toggle, wipeButton, new TableCell(null, scaleWidth: true), fit, actual, smaller, larger, zoom) }
        });
        showing.Width = 220;
        layout.AddRow(new TableLayout
        {
            Height = 28,
            Spacing = new Size(8, 0), Rows = { new TableRow(showing, new TableCell(divider, scaleWidth: true)) }
        });
        layout.Add(canvas, xscale: true, yscale: true);
        layout.AddRow(VisoLoomPanel.CreateLabel(result is null ? "Scroll to zoom · drag to pan · Fit restores the complete image." :
            "T toggles source/result · W toggles wipe · scroll to zoom · drag to pan."));
        layout.AddRow(close);
        Content = layout;
        AbortButton = close;
        close.Click += (_, _) => Close();
        fit.Click += (_, _) => { view.Fit(); RefreshImage(); };
        actual.Click += (_, _) => { view.ActualSize(); RefreshImage(); };
        smaller.Click += (_, _) => Zoom(1 / 1.25);
        larger.Click += (_, _) => Zoom(1.25);
        toggle.Click += (_, _) => ToggleImage();
        wipeButton.Click += (_, _) => { wipe = !wipe; RefreshImage(); };
        divider.ValueChanged += (_, _) => canvas.Invalidate();
        canvas.Paint += (_, e) =>
        {
            var bounds = view.Bounds;
            var rect = new RectangleF((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height);
            if (!wipe || result is null)
            {
                e.Graphics.DrawImage(result is not null && view.ShowResult ? result : source, rect);
                return;
            }
            DrawWipe(e.Graphics, source, result, rect, canvas.Size.Width * divider.Value / 100f);
        };
        canvas.SizeChanged += (_, _) => ResizeImage();
        LogicalPixelSizeChanged += (_, _) => ResizeImage();
        Shown += (_, _) => { ResizeImage(); canvas.Focus(); };
        canvas.KeyDown += (_, e) =>
        {
            if (result is not null && e.KeyData == Keys.T)
            { ToggleImage(); e.Handled = true; }
            if (result is not null && e.KeyData == Keys.W)
            { wipe = !wipe; RefreshImage(); e.Handled = true; }
        };
        canvas.MouseWheel += (_, e) =>
        {
            if (e.Delta.Height == 0) return;
            view.Zoom(Math.Pow(1.15, Math.Clamp(e.Delta.Height, -3, 3)), e.Location.X, e.Location.Y);
            RefreshImage(); e.Handled = true;
        };
        canvas.MouseDown += (_, e) =>
        {
            if ((e.Buttons & MouseButtons.Primary) == 0) return;
            canvas.Focus(); dragging = e.Location; e.Handled = true;
        };
        canvas.MouseMove += (_, e) =>
        {
            if (dragging is not { } previous) return;
            if ((e.Buttons & MouseButtons.Primary) == 0) { dragging = null; return; }
            view.Pan(e.Location.X - previous.X, e.Location.Y - previous.Y);
            dragging = e.Location; RefreshImage(); e.Handled = true;
        };
        canvas.MouseUp += (_, _) => dragging = null;
        canvas.MouseLeave += (_, _) => dragging = null;
        RefreshImage();

        void ResizeImage()
        {
            if (canvas.Size.Width <= 0 || canvas.Size.Height <= 0) return;
            view.SetViewport(canvas.Size.Width, canvas.Size.Height, LogicalPixelSize);
            RefreshImage();
        }
        void Zoom(double factor)
        {
            view.Zoom(factor, canvas.Size.Width / 2d, canvas.Size.Height / 2d);
            RefreshImage();
        }
        void RefreshImage()
        {
            showing.Text = result is null ? singleImageLabel : wipe ? "Rhino source ←  |  → Result" : view.ShowResult ? "Result" : "Rhino source capture";
            toggle.Text = view.ShowResult ? "Show source" : "Show result";
            wipeButton.Text = wipe ? "Exit wipe" : "Wipe";
            divider.Visible = wipe;
            zoom.Text = $"{view.ZoomPercent:0.#}%";
            canvas.Invalidate();
        }
        void ToggleImage()
        {
            wipe = false;
            view.Toggle();
            RefreshImage();
        }
    }

    internal static void DrawWipe(Graphics graphics, Bitmap source, Bitmap result, RectangleF frame, float position)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
            return;
        float split = Math.Clamp(position, frame.Left, frame.Right);
        float fraction = (split - frame.Left) / frame.Width;
        float sourceWidth = source.Width * fraction;
        float resultWidth = result.Width * fraction;
        if (split > frame.Left)
            graphics.DrawImage(source, new RectangleF(0, 0, sourceWidth, source.Height),
                new RectangleF(frame.Left, frame.Top, split - frame.Left, frame.Height));
        if (split < frame.Right)
            graphics.DrawImage(result, new RectangleF(resultWidth, 0, result.Width - resultWidth, result.Height),
                new RectangleF(split, frame.Top, frame.Right - split, frame.Height));
        if (split > frame.Left && split < frame.Right)
        {
            graphics.DrawLine(Colors.Black, split - 1, frame.Top, split - 1, frame.Bottom);
            graphics.DrawLine(Colors.White, split, frame.Top, split, frame.Bottom);
        }
    }

    public static void ShowComparison(Control owner, ComparisonSnapshot snapshot, string viewDescription, bool startWithWipe = false)
    {
        using var sourceStream = snapshot.OpenSource();
        using var resultStream = snapshot.OpenResult();
        using var source = new Bitmap(sourceStream);
        using var result = new Bitmap(resultStream);
        if (source.Width != snapshot.Frame.Width || source.Height != snapshot.Frame.Height || result.Size != source.Size)
            throw new ArgumentException("The comparison images did not decode to their recorded frame.");
        string info = $"Rhino capture · {viewDescription}\n{snapshot.CapturedAt.ToLocalTime():g} · {snapshot.Frame.Width:N0} × {snapshot.Frame.Height:N0} px";
        string processing = snapshot.IsUnchangedReference ? "Capture test · returned reference without image generation." :
            $"Model canvas: {snapshot.ModelCanvas.Width:N0} × {snapshot.ModelCanvas.Height:N0} px · comparison uses the complete restored frame. Enlargement does not add generated detail.";
        using var dialog = new ImageInspectionWindow(source, result, info, processing, "Result", startWithWipe);
        dialog.ShowModal(owner);
    }

    public static void ShowPreview(Control owner, byte[] png, string description)
    {
        ResolutionPlanner.ValidateSize(VisoLoom.Protocol.PngHeader.Read(png));
        using var stream = new MemoryStream(png.ToArray(), writable: false);
        using var image = new Bitmap(stream);
        using var dialog = new ImageInspectionWindow(image, null, description, "Includes any padding or scaling used to prepare the model input.", "Source image");
        dialog.ShowModal(owner);
    }

    public static void ShowResult(Control owner, byte[] png, PixelSize frame, string description)
    {
        ResolutionPlanner.ValidateSize(frame, ResolutionPlanner.ExportPixelLimit);
        if (VisoLoom.Protocol.PngHeader.ReadExport(png) != frame)
            throw new ArgumentException("The result does not match its recorded size.");
        using var stream = new MemoryStream(png, writable: false);
        using var image = new Bitmap(stream);
        if (image.Width != frame.Width || image.Height != frame.Height)
            throw new ArgumentException("The result did not decode to its recorded size.");
        using var dialog = new ImageInspectionWindow(image, null, description, "Choose 100% to inspect the saved pixels.", "Result");
        dialog.ShowModal(owner);
    }
}

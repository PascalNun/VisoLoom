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
    private readonly ImageInspection view;
    private PointF? dragging;

    private ImageInspectionWindow(Bitmap source, Bitmap? result, string description, string processing, string singleImageLabel)
    {
        Title = result is null ? "VisoLoom · " + singleImageLabel : "VisoLoom · Compare with Rhino capture";
        ClientSize = new Size(960, 720);
        MinimumSize = new Size(640, 400);
        Resizable = true;
        view = new(new(source.Width, source.Height));
        var fit = new Button { Text = "Fit" };
        var actual = new Button { Text = "100%" };
        var smaller = new Button { Text = "−" };
        var larger = new Button { Text = "+" };
        var close = new Button { Text = "Close" };
        var layout = new DynamicLayout { Padding = 12, Spacing = new Size(8, 8) };
        layout.AddRow(VisoLoomPanel.CreateLabel(description));
        layout.AddRow(VisoLoomPanel.CreateLabel(processing));
        toggle.Visible = result is not null;
        layout.AddRow(new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows = { new TableRow(toggle, new TableCell(null, scaleWidth: true), fit, actual, smaller, larger, zoom) }
        });
        layout.AddRow(showing);
        layout.Add(canvas, xscale: true, yscale: true);
        layout.AddRow(VisoLoomPanel.CreateLabel(result is null ? "Scroll to zoom · drag to pan · Fit restores the complete image." :
            "T toggles source/result when the image is focused · scroll to zoom · drag to pan. The capture stays fixed."));
        layout.AddRow(close);
        Content = layout;
        AbortButton = close;
        close.Click += (_, _) => Close();
        fit.Click += (_, _) => { view.Fit(); RefreshImage(); };
        actual.Click += (_, _) => { view.ActualSize(); RefreshImage(); };
        smaller.Click += (_, _) => Zoom(1 / 1.25);
        larger.Click += (_, _) => Zoom(1.25);
        toggle.Click += (_, _) => { view.Toggle(); RefreshImage(); };
        canvas.Paint += (_, e) =>
        {
            var bounds = view.Bounds;
            e.Graphics.DrawImage(result is not null && view.ShowResult ? result : source,
                new RectangleF((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height));
        };
        canvas.SizeChanged += (_, _) => ResizeImage();
        LogicalPixelSizeChanged += (_, _) => ResizeImage();
        Shown += (_, _) => { ResizeImage(); canvas.Focus(); };
        canvas.KeyDown += (_, e) =>
        {
            if (result is not null && e.KeyData == Keys.T)
            { view.Toggle(); RefreshImage(); e.Handled = true; }
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
            showing.Text = result is null ? singleImageLabel : view.ShowResult ? "Result" : "Rhino source capture";
            toggle.Text = view.ShowResult ? "Show source" : "Show result";
            zoom.Text = $"{view.ZoomPercent:0.#}%";
            canvas.Invalidate();
        }
    }

    public static void ShowComparison(Control owner, ComparisonSnapshot snapshot)
    {
        using var sourceStream = snapshot.OpenSource();
        using var resultStream = snapshot.OpenResult();
        using var source = new Bitmap(sourceStream);
        using var result = new Bitmap(resultStream);
        if (source.Width != snapshot.Frame.Width || source.Height != snapshot.Frame.Height || result.Size != source.Size)
            throw new ArgumentException("The comparison images did not decode to their recorded frame.");
        string info = $"Rhino capture · {snapshot.CapturedAt.ToLocalTime():g} · {snapshot.Frame.Width:N0} × {snapshot.Frame.Height:N0} px";
        string processing = snapshot.IsUnchangedReference ? "Capture test · returned reference without image generation." :
            $"Model canvas: {snapshot.ModelCanvas.Width:N0} × {snapshot.ModelCanvas.Height:N0} px · comparison uses the complete restored frame. Enlargement does not add generated detail.";
        using var dialog = new ImageInspectionWindow(source, result, info, processing, "Result");
        dialog.ShowModal(owner);
    }

    public static void ShowPreview(Control owner, byte[] png, string description)
    {
        ResolutionPlanner.ValidateSize(VisoLoom.Protocol.PngHeader.Read(png));
        using var stream = new MemoryStream(png.ToArray(), writable: false);
        using var image = new Bitmap(stream);
        using var dialog = new ImageInspectionWindow(image, null, description, "Includes any padding or scaling used to prepare the model input.", "Prepared input");
        dialog.ShowModal(owner);
    }

    public static void ShowResult(Control owner, byte[] png, PixelSize frame, string description)
    {
        ResolutionPlanner.ValidateSize(frame);
        if (VisoLoom.Protocol.PngHeader.Read(png) != frame)
            throw new ArgumentException("The result does not match its recorded size.");
        using var stream = new MemoryStream(png, writable: false);
        using var image = new Bitmap(stream);
        if (image.Width != frame.Width || image.Height != frame.Height)
            throw new ArgumentException("The result did not decode to its recorded size.");
        using var dialog = new ImageInspectionWindow(image, null, description, "Choose 100% to inspect the saved pixels.", "Result");
        dialog.ShowModal(owner);
    }
}

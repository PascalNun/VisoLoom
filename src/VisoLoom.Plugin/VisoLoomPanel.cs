using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using VisoLoom.Core;

namespace VisoLoom.Plugin;

[Guid("bbf16d98-c114-4f65-9790-6ee8dfe5806e")]
public sealed class VisoLoomPanel : Panel
{
    private readonly uint serial;
    private readonly CaptureCoordinator session;
    private readonly Label viewLabel = new() { Wrap = WrapMode.Word };
    private readonly Label status = new() { Wrap = WrapMode.Word };
    private readonly Label destination = new() { Wrap = WrapMode.Word };
    private readonly DropDown resolution = new() { Items = { "Viewport", "2× Viewport", "4× Viewport", "Custom (same aspect)" }, SelectedIndex = 0 };
    private readonly NumericStepper width = new() { MinValue = 1, MaxValue = 16384, DecimalPlaces = 0, Value = 1920 };
    private readonly NumericStepper height = new() { MinValue = 1, MaxValue = 16384, DecimalPlaces = 0, Value = 1080 };
    private readonly Label dimensions = new() { Wrap = WrapMode.Word };
    private readonly Button test = new() { Text = "Test capture" };
    private readonly Button cancel = new() { Text = "Cancel", Enabled = false };
    private readonly Button save = new() { Text = "Save PNG…", Enabled = false };
    private readonly Button reconnect = new() { Text = "Reconnect", Visible = false };
    private readonly Label capturedLabel = new() { Text = "Captured Rhino view", Wrap = WrapMode.Word, Font = SystemFonts.Bold() };
    private readonly Button change = new() { Text = "Change…" };
    private readonly ImageView source = new() { Height = 170 };
    private readonly ImageView result = new() { Height = 170 };
    private Bitmap? sourceBitmap;
    private Bitmap? resultBitmap;
    private Guid? shownScene;
    private Guid? shownJob;
    private readonly UITimer timer = new() { Interval = 1 };

    public VisoLoomPanel(RhinoDoc doc)
    {
        serial = doc.RuntimeSerialNumber; session = CaptureCoordinator.ForDocument(serial);
        Padding = 12;
        var layout = new DynamicLayout { Spacing = new Size(8, 8) };
        layout.AddRow(new Label { Text = "VisoLoom", Font = SystemFonts.Bold() });
        layout.AddRow(new Label { Text = "Generative Representation for Rhino", Wrap = WrapMode.Word });
        layout.AddRow(new Label { Text = "View", Font = SystemFonts.Bold() }); layout.AddRow(viewLabel);
        layout.AddRow(new Label { Text = "Mode" });
        layout.AddRow(new DropDown { Items = { "Capture test — unchanged reference" }, SelectedIndex = 0, Enabled = false });
        layout.AddRow(new TextArea { Text = "Modes and prompts become available with an image-model integration.", Height = 52, Enabled = false });
        layout.AddRow(new Label { Text = "Resolution", Font = SystemFonts.Bold() }); layout.AddRow(resolution);
        layout.AddRow(width, new Label { Text = "×" }, height); layout.AddRow(dimensions);
        layout.AddRow(new Label { Text = "Engine / destination", Font = SystemFonts.Bold() }); layout.AddRow(destination, change);
        layout.AddRow(new Label { Text = "Captured PNG and scene metadata → this computer", Wrap = WrapMode.Word });
        layout.AddRow(test, cancel); layout.AddRow(status); layout.AddRow(reconnect);
        layout.AddRow(capturedLabel); layout.AddRow(source);
        layout.AddRow(new Label { Text = "Returned reference · no generation", Font = SystemFonts.Bold() }); layout.AddRow(result);
        layout.AddRow(save); layout.AddRow(null);
        Content = new Scrollable { Content = layout, Border = BorderType.None };
        resolution.SelectedIndexChanged += (_, _) => RefreshView();
        width.ValueChanged += (_, _) => RefreshView(); height.ValueChanged += (_, _) => RefreshView();
        change.Click += (_, _) => Configure();
        test.Click += (_, _) =>
        {
            var document = RhinoDoc.FromRuntimeSerialNumber(serial);
            if (document is null) return;
            session.CustomSize = resolution.SelectedIndex == 3 ? new PixelSize((int)width.Value, (int)height.Value) : null;
            session.Multiplier = resolution.SelectedIndex switch { 1 => 2, 2 => 4, _ => 1 };
            session.Start(document);
        };
        reconnect.Click += (_, _) => session.Reconnect();
        cancel.Click += (_, _) => session.Cancel(); save.Click += (_, _) => Save();
        session.Changed += SessionChanged; timer.Elapsed += (_, _) => RefreshView(); timer.Start();
        UnLoad += (_, _) => timer.Stop(); Load += (_, _) => { timer.Start(); Refresh(); };
        Refresh();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { session.Changed -= SessionChanged; timer.Dispose(); sourceBitmap?.Dispose(); resultBitmap?.Dispose(); }
        base.Dispose(disposing);
    }

    private void SessionChanged(object? sender, EventArgs e) => Refresh();
    private void Refresh()
    {
        status.Text = session.Status; reconnect.Visible = session.CanReconnect; reconnect.Enabled = !session.IsBusy; destination.Text = "Local reference server\n" + session.Endpoint.GetLeftPart(UriPartial.Authority);
        test.Enabled = !session.IsBusy && session.IsConfigured; change.Enabled = !session.IsBusy; cancel.Enabled = session.IsBusy;
        save.Enabled = session.Result is not null; resolution.Enabled = !session.IsBusy;
        if (session.Capture is { } captured && captured.Manifest.SceneId != shownScene)
        {
            try
            {
                using var stream = new MemoryStream(captured.Png); var decoded = new Bitmap(stream);
                if (decoded.Width != captured.Manifest.Frame.Width || decoded.Height != captured.Manifest.Frame.Height) { decoded.Dispose(); throw new InvalidOperationException(); }
                capturedLabel.Text = $"Captured: {captured.ViewName} · {captured.Manifest.Frame.Width} × {captured.Manifest.Frame.Height} px";
                source.Image = decoded; sourceBitmap?.Dispose(); sourceBitmap = decoded; shownScene = captured.Manifest.SceneId;
                result.Image = null; resultBitmap?.Dispose(); resultBitmap = null; shownJob = null;
            }
            catch (Exception) { status.Text = "The host could not decode the captured PNG. The image is not displayed."; }
        }
        if (session.Result is { } output && output.Metadata.ResultId != shownJob)
        {
            try
            {
                using var stream = new MemoryStream(output.PngBytes); var decoded = new Bitmap(stream);
                if (decoded.Width != session.Capture!.Manifest.Frame.Width || decoded.Height != session.Capture.Manifest.Frame.Height) { decoded.Dispose(); throw new InvalidOperationException(); }
                result.Image = decoded; resultBitmap?.Dispose(); resultBitmap = decoded; shownJob = output.Metadata.ResultId;
            }
            catch (Exception) { status.Text = "The host could not decode the returned PNG. The image is not displayed."; save.Enabled = false; }
        }
        RefreshView();
    }

    private void RefreshView()
    {
        var document = RhinoDoc.FromRuntimeSerialNumber(serial); var view = document?.Views.ActiveView;
        if (view is null) { viewLabel.Text = "No model viewport"; test.Enabled = false; return; }
        var viewport = view.ActiveViewport;
        viewLabel.Text = viewport.Name + " · " + (viewport.IsParallelProjection ? "Parallel" : "Perspective") + " · " + viewport.DisplayMode.LocalName;
        bool custom = resolution.SelectedIndex == 3;
        width.Enabled = height.Enabled = custom && !session.IsBusy;
        try
        {
            var pixels = new PixelSize(viewport.Size.Width, viewport.Size.Height);
            var size = custom ? new PixelSize((int)width.Value, (int)height.Value) : ResolutionPlanner.ScaleViewport(pixels, resolution.SelectedIndex switch { 1 => 2, 2 => 4, _ => 1 });
            dimensions.Text = $"{size.Width:N0} × {size.Height:N0} px · complete Rhino frame";
            if ((long)pixels.Width * size.Height != (long)pixels.Height * size.Width) dimensions.Text = "Custom size must match the Rhino view's aspect ratio.";
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { dimensions.Text = "This size exceeds the capture limit. Choose a smaller size."; }
    }

    private void Configure()
    {
        var endpoint = new TextBox { Text = session.Endpoint.AbsoluteUri, Width = 300 };
        var secret = new PasswordBox { Width = 300 };
        var error = new Label { Wrap = WrapMode.Word, Text = "The token is held in memory for this document session only." };
        var connect = new Button { Text = "Use local server" }; var dismiss = new Button { Text = "Cancel" };
        using var dialog = new Dialog { Title = "VisoLoom · Local connection", Padding = 16, Resizable = false };
        var layout = new DynamicLayout { Spacing = new Size(8, 8) };
        layout.AddRow(new Label { Text = "This computer only · unchanged reference", Font = SystemFonts.Bold() });
        layout.AddRow(new Label { Text = "Server" }); layout.AddRow(endpoint);
        layout.AddRow(new Label { Text = "Session token" }); layout.AddRow(secret); layout.AddRow(error);
        layout.AddRow(new Label { Text = "Black Forest Labs, OpenAI, Google and xAI: not implemented.", Wrap = WrapMode.Word });
        layout.AddRow(connect, dismiss); dialog.Content = layout; dialog.DefaultButton = connect; dialog.AbortButton = dismiss;
        dismiss.Click += (_, _) => dialog.Close();
        connect.Click += (_, _) =>
        {
            try { session.Configure(new Uri(endpoint.Text), secret.Text); secret.Text = ""; dialog.Close(); }
            catch (Exception ex) when (ex is ArgumentException or UriFormatException or InvalidOperationException) { error.Text = ex.Message; }
        };
        dialog.ShowModal(this);
        secret.Text = "";
    }

    private void Save()
    {
        if (session.Result is not { } output || resultBitmap is null) return;
        using var dialog = new SaveFileDialog { Title = "Save unchanged reference", FileName = "VisoLoom-reference.png" };
        dialog.Filters.Add(new FileFilter("PNG image", ".png"));
        if (dialog.ShowDialog(this) != DialogResult.Ok) return;
        try
        {
            string target = Path.GetFullPath(dialog.FileName);
            string temporary = Path.Combine(Path.GetDirectoryName(target)!, ".visoloom-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { file.Write(output.PngBytes); file.Flush(true); }
                File.Move(temporary, target, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = "The image could not be saved. Check the destination and free disk space."; }
    }
}

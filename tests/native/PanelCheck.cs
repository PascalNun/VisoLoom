using System.Reflection;
using System.Text.Json;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using VisoLoom.Core;
using VisoLoom.Plugin;
using VisoLoom.Protocol;
using VisoLoom.Providers;
using VisoLoom.Tests;

namespace VisoLoom.NativeChecks;

public static class PanelCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(string report)
    {
        var doc = RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("Open a Rhino document first.");
        var session = CaptureCoordinator.ForDocument(doc.RuntimeSerialNumber);
        session.SelectedProvider = "openai";
        session.UserPrompt = "Fine ink lines and restrained watercolour. Keep the architecture and framing.";
        var frame = new PixelSize(960, 640);
        byte[] source = Draw(frame, Colors.SteelBlue);
        foreach (var color in new[] { Colors.DarkKhaki, Colors.Teal, Colors.Sienna, Colors.DarkOrange })
        {
            var capture = MakeCapture(source, frame);
            var png = Draw(frame, color);
            var result = new ProviderResult(png, new RenderResult
            {
                ResultId = Guid.NewGuid(), SceneId = capture.Manifest.SceneId, ProviderId = "fixture", ModelId = "authored", ModelVersion = "1",
                SceneDigest = ContentDigest.Sha256(JsonSerializer.SerializeToUtf8Bytes(capture.Manifest, WireJson.Options)),
                Plan = SceneFixture.Plan(frame), CompletedAt = DateTimeOffset.UtcNow,
                OutputSha256 = ContentDigest.Sha256(png), OutputByteCount = png.Length
            });
            session.RememberResult(capture, result, new(null, null, []));
        }
        var prepared = MakeCapture(source, frame);
        var intent = new RenderIntent { RequestedOutput = frame, EffectivePrompt = session.UserPrompt };
        var coordinator = typeof(CaptureCoordinator);
        object choice = coordinator.GetMethod("CurrentChoice", Private)!.Invoke(session, null)!;
        string fingerprint = (string)coordinator.GetMethod("PreviewFingerprint", Private)!.Invoke(session, [doc])!;
        var preview = Activator.CreateInstance(coordinator.GetNestedType("PreparedCapture", BindingFlags.NonPublic)!,
            [prepared, source, SceneFixture.Plan(frame), intent, choice, fingerprint, new PreviewGuard(fingerprint), "Fixture"]);
        coordinator.GetField("preview", Private)!.SetValue(session, preview);
        coordinator.GetProperty("Capture")!.SetValue(session, prepared);
        coordinator.GetProperty("Intent")!.SetValue(session, intent);
        using var panel = new VisoLoomPanel(doc, session, connectAutomatically: false);
        using var dialog = new Dialog { Title = "VisoLoom · Panel check", ClientSize = new Size(430, 785), MinimumSize = new Size(245, 480), Resizable = true };
        var narrow = new Button { Text = "260 pt" };
        var wide = new Button { Text = "430 pt" };
        var close = new Button { Text = "Done" };
        var layout = new DynamicLayout { Spacing = new Size(5, 4) };
        layout.AddRow(new TableLayout { Rows = { new TableRow(narrow, wide, close) } });
        layout.Add(panel, xscale: true, yscale: true);
        dialog.Content = layout;
        dialog.AbortButton = close;
        var primary = Field<Button>(panel, "primary");
        var images = Field<PanelImages>(panel, "images");
        var scroll = (Scrollable)panel.Content;
        var resolution = Field<DropDown>(panel, "resolution");
        var states = new List<object>();
        narrow.Click += (_, _) => dialog.ClientSize = new Size(260, 785);
        wide.Click += (_, _) => dialog.ClientSize = new Size(430, 785);
        close.Click += (_, _) => dialog.Close();
        dialog.Shown += (_, _) => Record("opened");
        panel.SizeChanged += (_, _) => Record("resized");
        primary.Click += (_, _) => Record("primary");
        images.Picker.SelectedIndexChanged += (_, _) => Record("image");
        resolution.SelectedIndexChanged += (_, _) => Record("size");
        try { dialog.ShowModal(Application.Instance.Windows.FirstOrDefault(w => w.Title.Contains("Rhino", StringComparison.OrdinalIgnoreCase))); }
        finally { Record("closed"); File.WriteAllText(report, JsonSerializer.Serialize(new { states })); }

        void Record(string action) => states.Add(new
        {
            action, width = panel.Width, primary = primary.Text, enabled = primary.Enabled,
            guidanceExpanded = Field<Expander>(panel, "guidance").Expanded,
            verticalFields = Field<List<StackLayout>>(panel, "fields").All(field => field.Orientation == Orientation.Vertical),
            horizontalOverflow = Math.Max(0, scroll.ScrollSize.Width - scroll.VisibleRect.Width),
            imageHeight = Field<ImageView>(images, "image").Height, isInput = images.IsShowingInput,
            choices = images.Picker.Items.Count, previewSubmitted = session.PreviewSubmitted,
            symbol = Field<ImageView>(panel, "symbol").Image is not null,
            compare = Field<Button>(images, "compare").Visible, save = Field<Button>(images, "save").Visible
        });
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;

    private static CapturedScene MakeCapture(byte[] png, PixelSize frame)
    {
        var scene = SceneFixture.Create();
        return new(scene with
        {
            Frame = frame, Camera = scene.Camera with { ViewportPixels = frame },
            Passes = [scene.Passes[0] with { Size = frame, ByteCount = png.Length, Sha256 = ContentDigest.Sha256(png) }]
        }, png, "Courtyard fixture");
    }

    private static byte[] Draw(PixelSize frame, Color accent)
    {
        using var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format24bppRgb);
        using (var graphics = new Graphics(bitmap))
        {
            graphics.Clear(Colors.WhiteSmoke);
            graphics.FillRectangle(Colors.Silver, 0, 455, 960, 185);
            graphics.FillRectangle(accent, 135, 130, 690, 285);
            for (int i = 0; i < 6; i++) graphics.FillRectangle(Colors.WhiteSmoke, 158 + i * 111, 169, 77, 215);
            graphics.FillRectangle(Colors.DimGray, 120, 110, 720, 21);
            graphics.FillRectangle(Colors.DarkGray, 185, 488, 150, 39);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}

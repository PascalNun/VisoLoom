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

    // No dialog, provider request or model edit. Exercise the real capture and
    // host image codec, including the sizes missed by the layout-only fixture.
    public static void RunSizes(string report)
    {
        try { RunSizeChecks(report); }
        catch (Exception ex) { File.WriteAllText(report, JsonSerializer.Serialize(new { error = ex.ToString() })); }
    }

    private static void RunSizeChecks(string report)
    {
        var doc = RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("Open a Rhino document first.");
        var session = CaptureCoordinator.ForDocument(doc.RuntimeSerialNumber);
        session.ChooseModel(doc, "local");
        using var panel = new VisoLoomPanel(doc, session, connectAutomatically: false);
        var sizes = Field<SegmentedButton>(panel, "resolution");
        var w = Field<TextBox>(panel, "width");
        var h = Field<TextBox>(panel, "height");
        var primary = Field<Button>(panel, "primary");
        void Apply() => typeof(VisoLoomPanel).GetMethod("RefreshView", Private)!.Invoke(panel, null);
        sizes.SelectedIndex = 3;
        w.Text = "1920";
        Apply();
        bool typedWidth = session.CustomSize?.Width == 1920 && w.Text == "1920" && primary.Enabled;
        session.Preview(doc);
        bool customPreview = session.PreviewPlan?.Final.Width == 1920 && session.PreviewIsCurrent(doc);
        h.Text = "900";
        Apply();
        bool typedHeight = session.CustomSize?.Height == 900 && h.Text == "900" && primary.Enabled;
        session.Preview(doc);
        bool heightPreview = session.PreviewPlan?.Final.Height == 900 && session.PreviewIsCurrent(doc);
        w.Text = "not a number";
        Apply();
        bool invalidInputExplained = !primary.Enabled && Field<WrappingLabel>(panel, "dimensions").Text.Contains("whole number");
        w.Text = "1920";
        Apply();
        bool recoveredInput = primary.Enabled;
        sizes.SelectedIndex = 1;
        var viewport = SceneBuilder.ViewportSize(session.SourceView(doc));
        var twice = ResolutionPlanner.ScaleViewport(viewport, 2);
        session.Preview(doc);
        bool twicePreview = session.PreviewPlan?.Final == twice && session.PreviewIsCurrent(doc);
        bool boundedCapture = session.PreviewPlan?.Capture.Pixels <= ResolutionPlanner.CapturePixelLimit;
        sizes.SelectedIndex = 2;
        bool fourTimesExplained = (long)viewport.Width * 4 * viewport.Height * 4 <= ResolutionPlanner.ExportPixelLimit
            ? primary.Enabled : !primary.Enabled && Field<WrappingLabel>(panel, "dimensions").Text.Contains("64 megapixels");

        var frame = new PixelSize(1001, 733);
        byte[] source;
        using (var bitmap = new System.Drawing.Bitmap(frame.Width, frame.Height))
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(System.Drawing.Color.White);
            foreach (var point in new[] { (40, 40), (960, 690), (500, 366) })
                graphics.FillRectangle(System.Drawing.Brushes.Red, point.Item1 - 12, point.Item2 - 12, 24, 24);
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            source = stream.ToArray();
        }
        var output = ResolutionPlanner.OutputWidth(frame, 800);
        var plan = ResolutionPlanner.Plan(frame, output, ManagedModels.KleinFourBit.Resolution);
        var codec = new RhinoImageProcessor();
        var prepared = codec.PrepareReference(source, plan);
        var normalized = codec.NormalizeResult(prepared, plan);
        bool Landmarks(byte[] png)
        {
            using var stream = new MemoryStream(png);
            using var bitmap = new System.Drawing.Bitmap(stream);
            var fit = ResolutionPlanner.FitCanvas(frame, output);
            return new[] { (40, 40), (960, 690), (500, 366) }.All(point =>
            {
                var pixel = bitmap.GetPixel((int)Math.Round(fit.X + point.Item1 * fit.Width / frame.Width),
                    (int)Math.Round(fit.Y + point.Item2 * fit.Height / frame.Height));
                return pixel.R > 200 && pixel.G < 60 && pixel.B < 60;
            });
        }
        bool uniformLandmarks = Landmarks(normalized);
        var cloudPlan = ResolutionPlanner.Plan(frame, output, OpenAiImageProvider.Model.Resolution);
        bool cloudUniformLandmarks = Landmarks(codec.NormalizeResult(codec.PrepareReference(source, cloudPlan), cloudPlan));
        var capture = MakeCapture(source, frame, "Authored pixels");
        var metadata = new RenderResult
        {
            ResultId = Guid.NewGuid(), SceneId = capture.Manifest.SceneId, ProviderId = "fixture", ModelId = "authored", ModelVersion = "1",
            SceneDigest = ContentDigest.Sha256(JsonSerializer.SerializeToUtf8Bytes(capture.Manifest, WireJson.Options)),
            Plan = plan, CompletedAt = DateTimeOffset.UtcNow, OutputByteCount = normalized.LongLength, OutputSha256 = ContentDigest.Sha256(normalized)
        };
        var comparison = ComparisonSnapshot.Create(capture.Manifest, source, new(normalized, metadata), codec);
        bool comparisonAligned = comparison.Frame == output;
        var freeFrame = new PixelSize(800, 450);
        var freePlan = ResolutionPlanner.PlanFreeFrame(frame, freeFrame, OpenAiImageProvider.Model.Resolution);
        byte[] freeResult = codec.NormalizeResult(codec.PrepareReference(source, freePlan), freePlan);
        var freeMetadata = metadata with { Plan = freePlan, OutputByteCount = freeResult.LongLength,
            OutputSha256 = ContentDigest.Sha256(freeResult) };
        var freeComparison = ComparisonSnapshot.Create(capture.Manifest, source, new(freeResult, freeMetadata), codec);
        using var alignedSource = freeComparison.OpenSource();
        var freePreview = codec.CreateComparisonPreview(source, frame, freeFrame);
        bool safeFrameComparison = PngHeader.ReadExport(((MemoryStream)alignedSource).ToArray()) == freeFrame &&
            PngHeader.Read(freePreview) == new PixelSize(640, 360);
        var large = ResolutionPlanner.OutputWidth(frame, 5000);
        var enlarged = codec.NormalizeResult(source, ResolutionPlanner.ReferencePlan(frame, large));
        var enlargedResult = new ProviderResult(enlarged, metadata with { Plan = ResolutionPlanner.ReferencePlan(frame, large),
            OutputByteCount = enlarged.LongLength, OutputSha256 = ContentDigest.Sha256(enlarged) });
        string root = Path.GetDirectoryName(report)!;
        if (OperatingSystem.IsMacOS() && root.StartsWith("/var/", StringComparison.Ordinal)) root = "/private" + root;
        var saved = new RenderImageStore(Path.Combine(root, "exports")).Save(enlargedResult);
        bool largeExportSaved = large.Pixels > ResolutionPlanner.CapturePixelLimit && PngHeader.ReadExport(enlarged) == large && saved.AutomaticPath is not null;
        File.WriteAllText(report, JsonSerializer.Serialize(new { typedWidth, customPreview, typedHeight, heightPreview,
            invalidInputExplained, recoveredInput, twicePreview, boundedCapture, fourTimesExplained,
            uniformLandmarks, cloudUniformLandmarks, comparisonAligned, safeFrameComparison, largeExportSaved }));
    }

    public static void RunModelPins(string report)
    {
        string path = Path.Combine(Path.GetDirectoryName(report)!, "model-pin.3dm");
        using var original = RhinoDoc.CreateHeadless(null) ?? throw new InvalidOperationException("Could not create the model fixture.");
        var session = CaptureCoordinator.ForDocument(original.RuntimeSerialNumber);
        bool startsWithCaptureTest = session.SelectedModelKey == "local" && session.ProjectPin is null;
        session.ChooseModel(original, "openai:" + OpenAiImageProvider.ModelId);
        var expected = new ProjectModelPin("openai", OpenAiImageProvider.ModelId, OpenAiImageProvider.ModelId);
        bool selectionStoresExactChoice = session.SelectedModel?.Identity == expected && session.ProjectPin == expected &&
            ProjectModelPin.Parse(original.Strings.GetValue(ProjectModelPin.DocumentKey)) == expected;
        if (!original.SaveAs(path))
            throw new InvalidOperationException("Could not save the model fixture.");

        using var reopened = RhinoDoc.OpenHeadless(path) ?? throw new InvalidOperationException("Could not reopen the model fixture.");
        var restored = CaptureCoordinator.ForDocument(reopened.RuntimeSerialNumber);
        bool reopenedExactSelection = restored.UsingProjectPin && restored.SelectedModel?.Identity == expected;
        reopened.Modified = false;
        restored.ChooseModel(reopened, "openai:" + expected.ModelId);
        bool sameChoiceDoesNotModify = !reopened.Modified && restored.SelectedModel?.Identity == expected;
        restored.ChooseModel(reopened, "google:" + GoogleImageProvider.ModelId);
        var google = new ProjectModelPin("google", GoogleImageProvider.ModelId, GoogleImageProvider.ModelId);
        bool changeStoresNewChoice = restored.SelectedModel?.Identity == google &&
            ProjectModelPin.Parse(reopened.Strings.GetValue(ProjectModelPin.DocumentKey)) == google;
        restored.ChooseModel(reopened, "local");
        bool diagnosticsKeepsRememberedModel = restored.SelectedProvider == "local" &&
            ProjectModelPin.Parse(reopened.Strings.GetValue(ProjectModelPin.DocumentKey)) == google;

        using var stale = RhinoDoc.CreateHeadless(null) ?? throw new InvalidOperationException("Could not create the model fixture.");
        var retired = expected with { ModelVersion = "retired-fixture-version" };
        stale.Strings.SetString(ProjectModelPin.DocumentKey, JsonSerializer.Serialize(retired, WireJson.Options));
        var retiredSession = CaptureCoordinator.ForDocument(stale.RuntimeSerialNumber);
        bool unavailablePinDoesNotFallBack = retiredSession.UsingProjectPin && retiredSession.ProjectPin == retired &&
            retiredSession.SelectedModel is null && !retiredSession.IsConfigured;
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            startsWithCaptureTest, selectionStoresExactChoice, reopenedExactSelection,
            sameChoiceDoesNotModify, changeStoresNewChoice, diagnosticsKeepsRememberedModel, unavailablePinDoesNotFallBack
        }));
    }

    public static void RunModelChooser(string report)
    {
        var local = new PanelChoice("managed:fixture", "This computer", "Local fixture", "This computer");
        var openAi = Enumerable.Range(1, 60).Select(i =>
            new PanelChoice($"openai:fixture-{i}", "Cloud", $"Fixture model {i}", "Cloud · OpenAI", Subgroup: "OpenAI"));
        var current = new PanelChoice("google:fixture", "Cloud", "Google fixture", "Cloud · Google",
            Subgroup: "Google", ModelId: "google-fixture-v1");
        var bfl = new PanelChoice("bfl:fixture", "Cloud", "BFL fixture", "Cloud · Black Forest Labs",
            Subgroup: "Black Forest Labs", ModelId: "bfl-fixture", Version: "2");
        using var chooser = new ModelChooser([local, .. openAi, current, bfl], current);
        var models = Field<ListBox>(chooser, "models");
        var details = Field<WrappingLabel>(chooser, "details");
        var choose = Field<Button>(chooser, "choose");
        var checks = new Dictionary<string, bool>();
        using var timer = new UITimer { Interval = 0.1 };
        chooser.Shown += (_, _) => timer.Start();
        timer.Elapsed += (_, _) =>
        {
            timer.Stop();
            try
            {
                var groups = chooser.Children.OfType<RadioButton>().ToDictionary(button => button.Text);
                checks["opensOnExactModel"] = groups["Google"].Checked && models.SelectedKey == current.Key &&
                    models.Items.Single().Text.StartsWith("✓", StringComparison.Ordinal);
                groups["OpenAI"].Checked = true;
                checks["longListIsIndependent"] = models.Items.Count == 60 && !choose.Enabled;
                groups["Black Forest Labs"].Checked = true;
                checks["switchesDirectlyToProvider"] = models.Items.Single().Key == bfl.Key && chooser.Result is null;
                models.SelectedKey = bfl.Key;
                checks["showsExactIdAndVersion"] = choose.Enabled && details.Text.Contains(bfl.ModelId) &&
                    details.Text.Contains("Version · 2");
                typeof(ListBox).GetMethod("OnActivated", Private)!.Invoke(models, [EventArgs.Empty]);
            }
            catch
            {
                checks["completed"] = false;
                chooser.Close(null);
            }
        };
        string? picked = chooser.ShowModal(Application.Instance.Windows.FirstOrDefault(window => window.Visible));
        checks["returnsExactModelOnActivation"] = picked == bfl.Key;
        File.WriteAllText(report, JsonSerializer.Serialize(checks));
    }

    public static void RunProjectHistory(string report)
    {
        string root = Path.GetDirectoryName(report)!;
        string path = Path.Combine(root, "project-history.3dm");
        using var original = RhinoDoc.CreateHeadless(null) ?? throw new InvalidOperationException("Could not create the history fixture.");
        var initial = new ProjectRenderRow(Guid.NewGuid(), Path.Combine(root, "original.png"),
            "Example model", "Perspective", "Rendered", 800, 600, DateTimeOffset.UtcNow.AddDays(-1), false);
        original.Strings.SetString("VisoLoom\\RenderIndexV1", JsonSerializer.Serialize(new[] { initial }, WireJson.Options));
        var rows = ProjectRenderIndex.Read(original);
        bool includesLegacy = rows.Count == 1 && rows[0] == initial;
        for (int i = 0; i < 62; i++)
        {
            rows = ProjectRenderIndex.Append(original, initial with
            {
                ResultId = Guid.NewGuid(), File = Path.Combine(root, $"example-{i}.png"), CompletedAt = initial.CompletedAt.AddMinutes(i + 1)
            }, rows);
        }
        var latest = rows[0] with { Model = "Updated model" };
        rows = ProjectRenderIndex.Append(original, latest, rows);
        original.Strings.SetString("VisoLoom.RenderHistoryV2", Guid.NewGuid().ToString("N"), "{invalid json");
        if (!original.SaveAs(path))
            throw new InvalidOperationException("Could not save the history fixture.");

        using var reopened = RhinoDoc.OpenHeadless(path) ?? throw new InvalidOperationException("Could not reopen the history fixture.");
        var restored = ProjectRenderIndex.Read(reopened);
        bool preservesAllReferences = restored.Count == 63 && restored[^1] == initial;
        bool deduplicatedAndNewestFirst = restored.Select(row => row.ResultId).Distinct().Count() == restored.Count &&
            restored.SequenceEqual(restored.OrderByDescending(row => row.CompletedAt)) && restored[0] == latest;
        bool ignoresMalformedEntry = restored.All(row => row.ResultId != Guid.Empty);
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            includesLegacy, preservesAllReferences, deduplicatedAndNewestFirst, ignoresMalformedEntry
        }));
    }

    public static void Run(string report)
    {
        var doc = RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("Open a Rhino document first.");
        var session = CaptureCoordinator.ForDocument(doc.RuntimeSerialNumber);
        session.SelectedModelKey = "openai";
        session.UserPrompt = "Fine ink lines and restrained watercolour. Keep the architecture and framing.";
        session.CustomSize = null;
        session.Multiplier = 1;
        var frame = new PixelSize(960, 640);
        byte[] source = Draw(frame, Colors.SteelBlue);
        foreach (var color in new[] { Colors.DarkKhaki, Colors.Teal, Colors.Sienna, Colors.DarkOrange })
        {
            var capture = MakeCapture(source, frame, color == Colors.DarkOrange ? "Rendered" : "Rendered with edges");
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
        var prepared = MakeCapture(source, frame, "Rendered");
        var intent = new RenderIntent { RequestedOutput = frame, EffectivePrompt = session.UserPrompt };
        var coordinator = typeof(CaptureCoordinator);
        object choice = coordinator.GetMethod("CurrentChoice", Private)!.Invoke(session, null)!;
        string fingerprint = (string)coordinator.GetMethod("PreviewFingerprint", Private)!.Invoke(session, [doc])!;
        var preview = Activator.CreateInstance(coordinator.GetNestedType("PreparedCapture", BindingFlags.NonPublic)!,
            [prepared, source, SceneFixture.Plan(frame), intent, choice, fingerprint, new PreviewGuard(fingerprint)]);
        coordinator.GetField("preview", Private)!.SetValue(session, preview);
        coordinator.GetProperty("Capture")!.SetValue(session, prepared);
        coordinator.GetProperty("Intent")!.SetValue(session, intent);
        using var panel = new VisoLoomPanel(doc, session, connectAutomatically: false);
        using var dialog = new Dialog { Title = "VisoLoom · Panel check", ClientSize = new Size(430, 785), MinimumSize = new Size(245, 480), Resizable = true };
        var narrow = new Button { Text = "260 pt" };
        var wide = new Button { Text = "430 pt" };
        var close = new Button { Text = "Done" };
        var error = new Button { Text = "Check error feedback" };
        var layout = new DynamicLayout { Spacing = new Size(5, 4) };
        layout.AddRow(new TableLayout { Rows = { new TableRow(narrow, wide, close) } });
        layout.AddRow(error);
        layout.Add(panel, xscale: true, yscale: true);
        dialog.Content = layout;
        dialog.AbortButton = close;
        var primary = Field<Button>(panel, "primary");
        var images = Field<PanelImages>(panel, "images");
        var scroll = Field<Scrollable>(panel, "scroll");
        var resolution = Field<SegmentedButton>(panel, "resolution");
        var thumbnails = Field<StackLayout>(images, "thumbnails");
        var tiles = Field<ToggleButton[]>(images, "tiles");
        var states = new List<object>();
        narrow.Click += (_, _) => dialog.ClientSize = new Size(260, 785);
        wide.Click += (_, _) => dialog.ClientSize = new Size(430, 785);
        close.Click += (_, _) => dialog.Close();
        dialog.Shown += (_, _) => Record("opened");
        panel.SizeChanged += (_, _) => Record("resized");
        primary.Click += (_, _) => Record("primary");
        images.SelectionChanged += (_, _) => Record("image");
        resolution.SelectedIndexChanged += (_, _) => Record("size");
        error.Click += (_, _) =>
        {
            coordinator.GetMethod("SetStatus", Private)!.Invoke(session, ["Fixture connection failed. Check Settings.", false]);
            Record("error");
        };
        try { dialog.ShowModal(Application.Instance.Windows.FirstOrDefault(w => w.Title.Contains("Rhino", StringComparison.OrdinalIgnoreCase))); }
        finally { Record("closed"); File.WriteAllText(report, JsonSerializer.Serialize(new { states })); }

        void Record(string action) => states.Add(new
        {
            action, width = panel.Width, primary = primary.Text, enabled = primary.Enabled,
            status = Field<WrappingLabel>(panel, "status").Text,
            guidanceExpanded = Field<Expander>(panel, "guidance").Expanded,
            verticalFields = Field<List<StackLayout>>(panel, "fields").All(field => field.Orientation == Orientation.Vertical),
            horizontalOverflow = Math.Max(0, scroll.ScrollSize.Width - scroll.VisibleRect.Width),
            imageHeight = Field<ImageView>(images, "image").Height, isInput = images.IsShowingInput,
            choices = images.ChoiceCount, previewSubmitted = session.PreviewSubmitted,
            thumbnailCount = thumbnails.Visible ? tiles.Count(tile => tile.Visible && tile.Image is not null) : 0,
            selectedThumbnail = Array.FindIndex(tiles, tile => tile.Checked),
            caption = Field<WrappingLabel>(images, "caption").Text,
            symbol = Field<ImageView>(panel, "symbol").Image is not null,
            compare = Field<Control>(images, "compare").Visible, save = Field<Button>(images, "save").Visible
        });
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;

    private static CapturedScene MakeCapture(byte[] png, PixelSize frame, string displayStyle)
    {
        var scene = SceneFixture.Create();
        return new(scene with
        {
            Frame = frame, Camera = scene.Camera with { ViewportPixels = frame },
            Passes = [scene.Passes[0] with { Size = frame, ByteCount = png.Length, Sha256 = ContentDigest.Sha256(png) }]
        }, png, "Courtyard fixture", displayStyle);
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

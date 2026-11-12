using System.Reflection;
using System.Text.Json;
using Eto.Drawing;
using Eto.Forms;
using VisoLoom.Core;
using VisoLoom.Plugin;
using VisoLoom.Protocol;
using VisoLoom.Providers;
using VisoLoom.Tests;

namespace VisoLoom.NativeChecks;

// Uses only authored pixels and temporary folders. The real image-folder
// preference, model connections and Rhino document are not changed.
public static class SavingCheck
{
    public static void Run(string report)
    {
        string root = Path.GetDirectoryName(report)!;
        if (OperatingSystem.IsMacOS() && root.StartsWith("/var/", StringComparison.Ordinal)) root = "/private" + root;
        string automatic = Path.Combine(root, "automatic"), chosen = Path.Combine(root, "chosen");
        string blocked = Path.Combine(root, "blocked"), missing = Path.Combine(root, "missing");
        Directory.CreateDirectory(chosen);
        Directory.CreateDirectory(Path.Combine(root, "exports"));
        File.WriteAllText(blocked, "Authored fixture: this is a file, not a folder.");
        var frame = new PixelSize(600, 400);
        byte[] png = Draw(frame);
        var scene = SceneFixture.Create() with { Frame = frame };
        scene = scene with
        {
            Camera = scene.Camera with { ViewportPixels = frame },
            Passes = [scene.Passes[0] with { Size = frame, ByteCount = png.Length, Sha256 = ContentDigest.Sha256(png) }]
        };
        var capture = new CapturedScene(scene, png, "Authored saving fixture", "Rendered");
        var variants = new[] { "Both copies saved", "Additional folder unavailable", "Automatic folder unavailable", "Neither copy saved" };
        var renders = new List<CompletedRender>();
        for (int i = 0; i < variants.Length; i++)
        {
            var output = new ProviderResult(png, new RenderResult
            {
                ResultId = Guid.NewGuid(), SceneId = scene.SceneId, ProviderId = "fixture", ModelId = "authored", ModelVersion = "1",
                SceneDigest = ContentDigest.Sha256(JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options)),
                Plan = SceneFixture.Plan(frame), CompletedAt = DateTimeOffset.UtcNow,
                OutputSha256 = ContentDigest.Sha256(png), OutputByteCount = png.Length
            });
            var saving = new RenderImageStore(i < 2 ? automatic : blocked).Save(output, i % 2 == 0 ? chosen : missing);
            renders.Add(new(capture, output, saving));
        }
        using var images = new PanelImages();
        var scenario = new DropDown { Width = 1 };
        foreach (string label in variants) scenario.Items.Add(label);
        var states = new List<object>();
        var folders = new List<string?>();
        var imagePage = new DynamicLayout { Padding = 12, Spacing = new Size(6, 6) };
        imagePage.AddRow(scenario);
        imagePage.AddRow(images);
        imagePage.AddRow(VisoLoomPanel.CreateLabel("Export only to this fixture's exports folder:\n" + Path.Combine(root, "exports")));
        imagePage.Add(null);
        using var dialog = new Dialog { Title = "VisoLoom · Saving check", ClientSize = new Size(440, 700), MinimumSize = new Size(360, 450), Resizable = true };
        var tabs = new TabControl();
        tabs.Pages.Add(new TabPage { Text = "Result", Content = imagePage });
        tabs.Pages.Add(new TabPage { Text = "Image saving", Content = new Panel { Padding = 12,
            Content = ImageSavingSettings.CreateControl(null, folder => folders.Add(folder), automatic) } });
        var done = new Button { Text = "Done" };
        var layout = new DynamicLayout { Padding = 8, Spacing = new Size(6, 6) };
        layout.Add(tabs, xscale: true, yscale: true);
        layout.AddRow(done);
        dialog.Content = layout;
        dialog.AbortButton = done;
        done.Click += (_, _) => dialog.Close();
        scenario.SelectedIndexChanged += (_, _) =>
        {
            if (scenario.SelectedIndex < 0) return;
            images.Update(null, [renders[scenario.SelectedIndex]]);
            Record("scenario");
        };
        for (int i = 0; i < renders.Count; i++) scenario.SelectedIndex = i;
        scenario.SelectedIndex = 0;
        Field<Button>("save").Click += (_, _) => Record("export");
        dialog.Shown += (_, _) => Record("opened");
        try { dialog.ShowModal(Application.Instance.Windows.FirstOrDefault(window => window.Title.Contains("Rhino", StringComparison.OrdinalIgnoreCase))); }
        finally
        {
            Record("closed");
            var exports = Directory.GetFiles(Path.Combine(root, "exports")).Select(path => new
            {
                name = Path.GetFileName(path), identical = File.ReadAllBytes(path).AsSpan().SequenceEqual(png)
            }).ToArray();
            bool copiesMatch = renders.All(render => new[] { render.Saving.AutomaticPath, render.Saving.OutputPath }
                .Where(path => path is not null).All(path => File.ReadAllBytes(path!).AsSpan().SequenceEqual(png)));
            File.WriteAllText(report, JsonSerializer.Serialize(new { states, folders, exports, copiesMatch, missingFolderNotCreated = !Directory.Exists(missing) }));
        }
        T Field<T>(string name) => (T)typeof(PanelImages).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(images)!;
        void Record(string action) => states.Add(new
        {
            action, selected = scenario.SelectedIndex, note = Field<WrappingLabel>("note").Text,
            image = Field<ImageView>("image").Image is not null, save = Field<Button>("save").Visible,
            exportCount = Directory.GetFiles(Path.Combine(root, "exports")).Length
        });
    }

    private static byte[] Draw(PixelSize size)
    {
        using var image = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
        using (var graphics = new Graphics(image))
        {
            graphics.Clear(Colors.WhiteSmoke);
            graphics.FillRectangle(Colors.SlateGray, 60, 80, 480, 240);
            for (int i = 0; i < 5; i++) graphics.FillRectangle(Colors.WhiteSmoke, 80 + i * 94, 110, 65, 165);
        }
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}

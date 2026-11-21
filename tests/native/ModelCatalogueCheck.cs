using System.Text.Json;
using Eto.Drawing;
using Eto.Forms;
using VisoLoom.Plugin;

namespace VisoLoom.NativeChecks;

public static class CatalogueCheck
{
    public static void Run(string report)
    {
        using var lifetime = new CancellationTokenSource();
        using var dialog = new Dialog { Title = "VisoLoom · Catalogue check", ClientSize = new Size(480, 650),
            MinimumSize = new Size(360, 400), Resizable = true };
        var control = new LocalModelsControl(lifetime.Token);
        var done = new Button { Text = "Done" };
        var narrow = new Button { Text = "360 pt" };
        narrow.Click += (_, _) => dialog.ClientSize = new Size(360, 650);
        var scroll = new Scrollable { Content = control, Border = BorderType.None, ExpandContentWidth = true };
        var layout = new DynamicLayout { Padding = 12, Spacing = new Size(8, 8) };
        layout.AddRow(SettingsLayout.Actions(narrow, done));
        layout.Add(scroll, xscale: true, yscale: true);
        dialog.Content = layout;
        dialog.AbortButton = done;
        done.Click += (_, _) => dialog.Close();
        var states = new List<object>();
        dialog.Shown += (_, _) => Record("opened");
        dialog.SizeChanged += (_, _) => Record("resize");
        foreach (var button in SettingsCheck.Descendants(control).OfType<Button>()
            .Where(button => button.Text is "Browse models…" or "Use existing files…"))
            button.Click += (_, _) => Record(button.Text);
        try { dialog.ShowModal(Application.Instance.Windows.FirstOrDefault(w => w.Title.Contains("Rhino", StringComparison.OrdinalIgnoreCase))); }
        finally
        {
            Record("closed");
            lifetime.Cancel();
            File.WriteAllText(report, JsonSerializer.Serialize(new { states }));
        }

        bool Shown(string text) => SettingsCheck.Descendants(control).OfType<Button>().Any(button => button.Text == text && SettingsCheck.Visible(button));
        void Record(string action) => states.Add(new { action, width = dialog.ClientSize.Width,
            catalogue = Shown("Change…"), existingFiles = Shown("Choose folder…"),
            horizontalOverflow = Math.Max(0, scroll.ScrollSize.Width - scroll.VisibleRect.Width) });
    }
}

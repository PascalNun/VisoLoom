using System.Reflection;
using System.Text.Json;
using Eto.Drawing;
using Eto.Forms;
using VisoLoom.Plugin;

namespace VisoLoom.NativeChecks;

public static class SettingsCheck
{
    public static void Run(string report)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        bool savingChanged = false;
        using var settings = new ConnectionSettingsControl(imageSaving: ImageSavingSettings.CreateControl(null, _ => savingChanged = true));
        var tabs = (TabControl)typeof(ConnectionSettingsControl).GetField("tabs", flags)!.GetValue(settings)!;
        var secret = (PasswordBox)typeof(ConnectionSettingsControl).GetField("secret", flags)!.GetValue(settings)!;
        using var dialog = new Dialog { Title = "VisoLoom · Settings check", ClientSize = new Size(480, 600), MinimumSize = new Size(360, 400), Resizable = true };
        var narrow = new Button { Text = "360 pt" };
        var done = new Button { Text = "Done" };
        var layout = new DynamicLayout { Padding = 8, Spacing = new Size(6, 6) };
        layout.AddRow(new TableLayout { Rows = { new TableRow(narrow, done) } });
        layout.Add(settings, xscale: true, yscale: true);
        dialog.Content = layout;
        dialog.AbortButton = done;
        narrow.Click += (_, _) => dialog.ClientSize = new Size(360, 600);
        done.Click += (_, _) => dialog.Close();
        var states = new List<object>();
        tabs.SelectedIndexChanged += (_, _) => Record("page");
        secret.GotFocus += (_, _) => Record("keyFocused");
        dialog.Shown += (_, _) => Record("opened");
        dialog.SizeChanged += (_, _) => Record("resized");
        foreach (var button in Descendants(settings).OfType<Button>().Where(button => button.Text == "Set up OpenAI…"))
            button.Click += (_, _) => Record("cloudHandoff");
        long revision = ConnectionSettings.Revision;
        try { dialog.ShowModal(Application.Instance.Windows.FirstOrDefault(window => window.Title.Contains("Rhino", StringComparison.OrdinalIgnoreCase))); }
        finally
        {
            Record("closed");
            File.WriteAllText(report, JsonSerializer.Serialize(new { states, savingChanged, connectionUnchanged = revision == ConnectionSettings.Revision }));
        }
        void Record(string action) => states.Add(new
        {
            action, width = dialog.ClientSize.Width, selected = tabs.SelectedIndex,
            pages = tabs.Pages.Select(page => page.Text).ToArray(),
            secretFocused = secret.HasFocus,
            horizontalOverflow = Descendants(tabs.SelectedPage).OfType<Scrollable>()
                .Select(scroll => Math.Max(0, scroll.ScrollSize.Width - scroll.VisibleRect.Width)).DefaultIfEmpty().Max()
        });
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        yield return control;
        if (control is Container container)
            foreach (var child in container.Controls)
                foreach (var descendant in Descendants(child)) yield return descendant;
    }
}

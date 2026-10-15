using System.Reflection;
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
        var control = new LocalModelsControl(lifetime.Token, () => dialog.Close());
        var done = new Button { Text = "Done" };
        var narrow = new Button { Text = "Check narrow width" };
        narrow.Click += (_, _) => dialog.ClientSize = new Size(360, 650);
        var layout = new DynamicLayout { Padding = 12, Spacing = new Size(8, 8) };
        layout.AddRow(narrow);
        layout.Add(new Scrollable { Content = control, Border = BorderType.None, ExpandContentWidth = true }, yscale: true);
        layout.AddRow(done);
        dialog.Content = layout;
        dialog.AbortButton = done;
        done.Click += (_, _) => dialog.Close();
        var states = new List<object>();
        dialog.Shown += (_, _) => Record("opened");
        Field<ListBox>("catalogue").SelectedIndexChanged += (_, _) => Record("choice");
        dialog.SizeChanged += (_, _) => Record("resize");
        try { dialog.ShowModal(Application.Instance.Windows.FirstOrDefault(w => w.Title.Contains("Rhino", StringComparison.OrdinalIgnoreCase))); }
        finally
        {
            Record("closed");
            lifetime.Cancel();
            File.WriteAllText(report, JsonSerializer.Serialize(new { states }));
        }
        T Field<T>(string name) => (T)typeof(LocalModelsControl).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(control)!;
        void Record(string action) => states.Add(new { action, width = dialog.ClientSize.Width,
            selected = Field<ListBox>("catalogue").SelectedIndex,
            localVisible = Field<Panel>("localFolders").Visible,
            cloudVisible = Field<Button>("configureCloud").Visible,
            planEnabled = Field<Button>("plan").Enabled,
            detail = Field<WrappingLabel>("details").Text });
    }
}

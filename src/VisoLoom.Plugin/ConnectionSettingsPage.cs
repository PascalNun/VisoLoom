using Eto.Drawing;
using Eto.Forms;
using Rhino.UI;

namespace VisoLoom.Plugin;

internal sealed class ConnectionSettingsPage() : OptionsDialogPage("VisoLoom")
{
    private ConnectionSettingsControl? control;
    public override object PageControl => control ??= new();
}

internal sealed class ConnectionSettingsControl : Panel
{
    private readonly ConnectionsView connections;
    private readonly TabControl tabs = new();
    private readonly CancellationTokenSource lifetime = new();

    public ConnectionSettingsControl(Action<Control>? configureLocal = null, Control? imageSaving = null)
    {
        // The page hosts the tabs; the Connections view owns every connection
        // flow, and the Models tab hands off to it with the destination it
        // described, not just to whatever was on the Connections tab.
        var models = new LocalModelsControl(lifetime.Token, OpenCloudConnection, OpenLocalConnection);
        connections = new ConnectionsView(lifetime.Token, configureLocal);
        tabs.Pages.Add(Page("Models", models));
        tabs.Pages.Add(Page("Connections", connections));
        tabs.Pages.Add(Page("Image saving", imageSaving ?? ImageSavingSettings.CreateControl()));
        Content = tabs;
    }

    private static TabPage Page(string title, Control content) => new()
    {
        Text = title,
        Content = new Scrollable { Content = new Panel { Padding = 12, Content = content }, Border = BorderType.None, ExpandContentWidth = true }
    };

    private void OpenCloudConnection(string providerId)
    {
        tabs.SelectedIndex = 1;
        connections.Select(providerId);
    }

    private void OpenLocalConnection()
    {
        tabs.SelectedIndex = 1;
        connections.Select("local");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { lifetime.Cancel(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }

    public static void ShowSettings(Control owner, Action<Control>? configureLocal = null)
    {
        using var dialog = new Dialog { Title = "VisoLoom · Settings", ClientSize = new Size(480, 600), MinimumSize = new Size(360, 400), Resizable = true };
        var done = new Button { Text = "Done" };
        var layout = new DynamicLayout { Padding = 8, Spacing = new Size(8, 8) };
        layout.Add(new ConnectionSettingsControl(configureLocal), yscale: true);
        layout.AddRow(done);
        dialog.Content = layout;
        dialog.AbortButton = done;
        done.Click += (_, _) => dialog.Close();
        dialog.ShowModal(owner);
    }
}

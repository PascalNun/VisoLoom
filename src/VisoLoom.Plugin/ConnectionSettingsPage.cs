using Eto.Drawing;
using Eto.Forms;
using Rhino.UI;

namespace VisoLoom.Plugin;

internal sealed class ConnectionSettingsPage() : OptionsDialogPage("VisoLoom")
{
    private ConnectionSettingsControl? control;
    public override object PageControl => control ??= new();
}

// ADR 0028's saved-connections list, the Models page and image saving in one
// scrolling page — the Settings mockup's single-page design (model-selection
// plan, Inline Settings row): the Rhino Options page and the panel's Settings
// button host the same control, and every provider or model is configured in
// place. Your server appears with its designed empty state; connecting a
// studio server is the separate office-server work item.
internal sealed class ConnectionSettingsControl : Panel
{
    private readonly ConnectionsView connections;
    private readonly Panel cloudSection = new();
    private readonly CancellationTokenSource lifetime = new();

    public ConnectionSettingsControl(Action<Control>? configureLocal = null, Control? imageSaving = null)
    {
        var models = new LocalModelsControl(lifetime.Token, OpenCloudConnection, OpenLocalConnection);
        connections = new ConnectionsView(lifetime.Token, configureLocal);
        cloudSection.Content = connections;
        var layout = new DynamicLayout { DefaultSpacing = new Size(8, 12) };
        layout.Add(Section("Local models", models));
        layout.Add(Section("Cloud providers", cloudSection));
        layout.Add(ServerSection());
        layout.Add(Section("Image saving", imageSaving ?? ImageSavingSettings.CreateControl()));
        Content = new Scrollable
        {
            Content = new Panel { Padding = 12, Content = layout },
            Border = BorderType.None,
            ExpandContentWidth = true
        };
    }

    private static Panel Section(string title, Control content)
    {
        var layout = new DynamicLayout { DefaultSpacing = new Size(6, 8) };
        layout.AddRow(new TableLayout { Rows = { new TableRow(Heading(title)) } });
        layout.AddRow(new TableLayout { Rows = { new TableRow(content) } });
        return new() { Content = layout };
    }

    private static Label Heading(string text)
    {
        var label = new Label { Text = text, Font = SystemFonts.Bold() };
        return label;
    }

    // The designed future destination for offices: an explicitly added server
    // on another machine. The office-server protocol is a separate planned
    // package (ADR 0022's server plan), so the section states that it exists
    // and adds nothing until that package ships.
    private static Panel ServerSection()
    {
        var status = VisoLoomPanel.CreateLabel("No server added. Use a model running on another machine.");
        var note = VisoLoomPanel.CreateLabel("Adding a studio server arrives with the planned office-server package. Nothing is connected here.");
        note.TextColor = SystemColors.DisabledText;
        var layout = new DynamicLayout { DefaultSpacing = new Size(6, 8) };
        layout.AddRow(new TableLayout { Rows = { new TableRow(Heading("Your server")) } });
        layout.AddRow(new TableLayout { Rows = { new TableRow(status) } });
        layout.AddRow(new TableLayout { Rows = { new TableRow(note) } });
        return new() { Content = layout };
    }

    private void OpenCloudConnection(string providerId)
    {
        // The provider's row sits in the Cloud providers section of this page;
        // selecting it follows ADR 0028's in-place flow from the handoff.
        connections.Select(providerId);
    }

    private void OpenLocalConnection()
    {
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

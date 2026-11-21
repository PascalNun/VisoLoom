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
    private readonly CancellationTokenSource lifetime = new();

    public ConnectionSettingsControl(Action<Control>? configureLocal = null, Control? imageSaving = null)
    {
        var connections = new ConnectionsView(lifetime.Token, configureLocal);
        var models = new LocalModelsControl(lifetime.Token, connections.LocalSetup);
        Content = new Scrollable
        {
            Content = new Panel
            {
                Padding = new Padding(16, 12),
                Content = SettingsLayout.Stack(
                    SettingsLayout.Stack(VisoLoomPanel.CreateLabel("Local models", bold: true), models),
                    SettingsLayout.Rule(),
                    SettingsLayout.Stack(VisoLoomPanel.CreateLabel("Cloud providers", bold: true), connections),
                    SettingsLayout.Rule(),
                    SettingsLayout.Stack(
                        VisoLoomPanel.CreateLabel("Your server", bold: true),
                        VisoLoomPanel.CreateLabel("No server added", bold: true),
                        VisoLoomPanel.CreateLabel("Connecting to a model on another machine is planned.")),
                    SettingsLayout.Rule(),
                    imageSaving ?? ImageSavingSettings.CreateControl())
            },
            Border = BorderType.None,
            ExpandContentWidth = true
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { lifetime.Cancel(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }

    public static void ShowSettings(Control owner, Action<Control>? configureLocal = null)
    {
        using var dialog = new Dialog { Title = "VisoLoom Settings", ClientSize = new Size(480, 620), MinimumSize = new Size(360, 400), Resizable = true };
        var done = new Button { Text = "Done" };
        var layout = new DynamicLayout();
        layout.Add(new ConnectionSettingsControl(configureLocal), xscale: true, yscale: true);
        layout.AddRow(SettingsLayout.Rule());
        layout.AddRow(new Panel { Padding = new Padding(16, 8), Content = SettingsLayout.Row(new Panel(), done) });
        dialog.Content = layout;
        dialog.AbortButton = done;
        done.Click += (_, _) => dialog.Close();
        dialog.ShowModal(owner);
    }
}

internal static class SettingsLayout
{
    public static StackLayout Stack(params Control[] controls)
    {
        var stack = new StackLayout { Orientation = Orientation.Vertical, Spacing = 6, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        foreach (var control in controls)
            stack.Items.Add(control);
        return stack;
    }

    public static StackLayout Caption(string title, Control detail) => new()
    {
        Orientation = Orientation.Vertical,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items = { VisoLoomPanel.CreateLabel(title, bold: true), detail }
    };

    public static StackLayout Row(Control label, Control action) => new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Center,
        Spacing = 8,
        Items = { new StackLayoutItem(label, expand: true), action }
    };

    public static StackLayout Actions(params Control[] controls)
    {
        var row = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 6, VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var control in controls)
            row.Items.Add(control);
        return row;
    }

    public static Panel Inset(Control content) => new()
    {
        Content = new TableLayout
        {
            Spacing = new Size(8, 0),
            Rows = { new TableRow(new Panel { Width = 1, BackgroundColor = RuleColor }, new TableCell(content, true)) }
        }
    };

    public static Panel Rule() => new() { Height = 1, BackgroundColor = RuleColor };

    private static Color RuleColor => Color.Blend(SystemColors.ControlBackground, SystemColors.DisabledText, 0.35f);
}

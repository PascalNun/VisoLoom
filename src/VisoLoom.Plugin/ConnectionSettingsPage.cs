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

    public ConnectionSettingsControl(Control? imageSaving = null)
    {
        var connections = new ConnectionsView(lifetime.Token);
        var models = new LocalModelsControl(lifetime.Token);
        Content = new Scrollable
        {
            Content = new Panel
            {
                Padding = new Padding(16, 12),
                Content = SettingsLayout.Stack(
                    SettingsLayout.Stack(VisoLoomPanel.CreateLabel("Local models", bold: true), models),
                    SettingsLayout.SectionDivider(),
                    SettingsLayout.Stack(VisoLoomPanel.CreateLabel("Cloud providers", bold: true), connections),
                    SettingsLayout.SectionDivider(),
                    SettingsLayout.Stack(
                        VisoLoomPanel.CreateLabel("Your server", bold: true),
                        ServerSettings.CreateControl()),
                    SettingsLayout.SectionDivider(),
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

    public static void ShowSettings(Control owner)
    {
        using var dialog = new Dialog { Title = "VisoLoom Settings", ClientSize = new Size(480, 620), MinimumSize = new Size(360, 400), Resizable = true };
        var done = new Button { Text = "Done" };
        var layout = new DynamicLayout();
        layout.Add(new ConnectionSettingsControl(), xscale: true, yscale: true);
        layout.AddRow(SettingsLayout.Rule(0.28f));
        layout.AddRow(new Panel { Padding = new Padding(16, 8), Content = SettingsLayout.Row(new Panel(), done) });
        dialog.Content = layout;
        dialog.AbortButton = done;
        done.Click += (_, _) => dialog.Close();
        dialog.ShowModal(owner);
    }
}

internal static class SettingsLayout
{
    public static WrappingLabel Secondary(string text)
    {
        var label = VisoLoomPanel.CreateLabel(text);
        label.TextColor = SecondaryColor;
        return label;
    }

    public static void ConnectionStatus(WrappingLabel label, bool confirmed)
    {
        var text = SystemColors.ControlText;
        bool dark = text.R + text.G + text.B > 1.5f;
        label.TextColor = confirmed
            ? dark ? Color.FromRgb(0xA1D5AF) : Color.FromRgb(0x25643D)
            : SecondaryColor;
    }

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
        Padding = new Padding(0, 2),
        Content = new TableLayout
        {
            Spacing = new Size(10, 0),
            Rows = { new TableRow(new Panel { Width = 2, BackgroundColor = LineColor(0.32f) }, new TableCell(content, true)) }
        }
    };

    public static Panel SectionDivider() => new() { Padding = new Padding(0, 5), Content = Rule(0.28f) };

    public static Panel Rule(float contrast = 0.17f) => new() { Height = 1, BackgroundColor = LineColor(contrast) };

    public static Color DividerColor => LineColor(0.23f);

    public static Color SecondaryColor => LineColor(0.76f);

    private static Color LineColor(float weight) => Color.Blend(SystemColors.ControlBackground, SystemColors.ControlText, weight);
}

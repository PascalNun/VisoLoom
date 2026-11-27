using Eto.Drawing;
using Eto.Forms;

namespace VisoLoom.Plugin;

internal sealed record PanelModelChoice(string Key, string Section, string Name, string Destination, string Status = "", string Provider = "");

internal sealed class PanelModelPicker : StackLayout
{
    private readonly Button opener = new() { Text = "Choose a model…", Width = 1 };
    private readonly WrappingLabel destination = VisoLoomPanel.CreateLabel("");
    private readonly StackLayout rows = SettingsLayout.Stack();
    private readonly Scrollable options;
    private IReadOnlyList<PanelModelChoice> choices = [];
    private PanelModelChoice? selection;

    public PanelModelPicker()
    {
        Orientation = Orientation.Vertical;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Spacing = 2;
        rows.Spacing = 4;
        options = new Scrollable
        {
            Border = BorderType.None,
            Content = rows,
            ExpandContentWidth = true,
            Visible = false,
            Width = 1
        };
        Items.Add(opener);
        Items.Add(destination);
        opener.Click += (_, _) =>
        {
            if (options.Visible)
            {
                Dismiss();
                return;
            }

            PopulateRows();
            options.Visible = true;
        };
    }

    public Control Options => options;

    public event Action<string>? ModelChosen;

    public void Dismiss() => options.Visible = false;

    public void Update(IReadOnlyList<PanelModelChoice> next, PanelModelChoice current)
    {
        if (selection == current && choices.SequenceEqual(next))
            return;
        selection = current;
        choices = next;
        opener.Text = current.Name + " ▾";
        opener.ToolTip = current.Name;
        destination.Text = current.Status.Length == 0
            ? current.Destination : $"{current.Destination} · {current.Status}";
        destination.Visible = destination.Text.Length != 0;
        SettingsLayout.ConnectionStatus(destination, confirmed: false);

        // Keep the native keyboard focus and scroll position during a connection
        // refresh unless the actual model list or selected row changed.
        if (options.Visible)
            PopulateRows();
    }

    private void PopulateRows()
    {
        var old = rows.Items.Select(item => item.Control).ToArray();
        rows.Items.Clear();
        foreach (var control in old)
            control.Dispose();

        int estimatedHeight = 0;
        foreach (var section in choices.GroupBy(choice => choice.Section))
        {
            if (estimatedHeight > 0)
                rows.Items.Add(SettingsLayout.SectionDivider());
            rows.Items.Add(VisoLoomPanel.CreateLabel(section.Key, bold: true));
            estimatedHeight += 32;
            foreach (var provider in section.GroupBy(choice => choice.Provider))
            {
                if (provider.Key.Length > 0)
                {
                    rows.Items.Add(VisoLoomPanel.CreateLabel(provider.Key, bold: true));
                    estimatedHeight += 23;
                }
                foreach (var choice in provider)
                {
                    bool selected = choice.Key == selection?.Key;
                    string label = choice.Status.Length == 0 ? choice.Name : $"{choice.Name} · {choice.Status}";
                    var button = new ToggleButton
                    {
                        Width = 1,
                        MinimumSize = Size.Empty,
                        Text = (selected ? "✓  " : "") + label,
                        ToolTip = $"{label} · {choice.Destination}",
                        Checked = selected
                    };
                    button.Click += (_, _) =>
                    {
                        Dismiss();
                        if (Enabled)
                            ModelChosen?.Invoke(choice.Key);
                        opener.Focus();
                    };
                    rows.Items.Add(button);
                    estimatedHeight += 31;
                }
            }
        }
        options.Height = Math.Min(350, estimatedHeight);
    }
}

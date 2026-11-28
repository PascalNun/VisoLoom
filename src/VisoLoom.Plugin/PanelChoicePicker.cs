using Eto.Drawing;
using Eto.Forms;

namespace VisoLoom.Plugin;

internal sealed record PanelChoice(string Key, string Section, string Name, string Destination, string Status = "",
    string Subgroup = "", string Description = "", string ModelId = "", string Version = "");

internal sealed class PanelChoicePicker : StackLayout
{
    private const int RowHeight = 23;
    private sealed record ChoiceRow(PanelChoice? Choice, string Label);
    private readonly Button opener = new() { Width = 1 };
    private readonly WrappingLabel destination = VisoLoomPanel.CreateLabel("");
    private readonly StackLayout rows = SettingsLayout.Stack();
    private readonly Panel options;
    private IReadOnlyList<PanelChoice> choices = [];
    private PanelChoice? selection;

    public PanelChoicePicker()
    {
        Orientation = Orientation.Vertical;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Spacing = 2;
        rows.Spacing = 4;
        options = SettingsLayout.Inset(rows);
        options.Visible = false;
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
            Opened?.Invoke();
        };
        opener.KeyDown += (_, e) =>
        {
            if (e.Key == Keys.Escape && options.Visible)
            {
                Dismiss();
                e.Handled = true;
            }
        };
    }

    public Control Options => options;
    public event Action<string>? ChoiceSelected;
    public event Action? Opened;
    public void Dismiss() => options.Visible = false;

    public void Update(IReadOnlyList<PanelChoice> next, PanelChoice current)
    {
        if (selection == current && choices.SequenceEqual(next))
            return;
        selection = current;
        choices = next;
        opener.Text = current.Name + " ▾";
        opener.ToolTip = ChoiceDetails(current);
        destination.Text = current.Status.Length == 0 ? current.Destination : $"{current.Destination} · {current.Status}";
        destination.Visible = destination.Text.Length != 0;
        SettingsLayout.ConnectionStatus(destination, confirmed: false);

        if (options.Visible)
            PopulateRows();
    }

    private void PopulateRows()
    {
        var old = rows.Items.Select(item => item.Control).ToArray();
        rows.Items.Clear();
        foreach (var control in old)
            control.Dispose();

        foreach (var section in choices.GroupBy(choice => choice.Section))
        {
            if (rows.Items.Count > 0)
                rows.Items.Add(SettingsLayout.Rule());
            if (section.Key == "Diagnostics")
            {
                rows.Items.Add(new Expander { Header = section.Key, Expanded = false, Content = ChoiceRows(section.Select(ModelRow), 1) });
                continue;
            }

            rows.Items.Add(VisoLoomPanel.CreateLabel(section.Key, bold: true));
            if (section.Key == "Cloud")
            {
                var cloud = section.GroupBy(choice => choice.Subgroup)
                    .SelectMany(group => new[] { new ChoiceRow(null, group.Key) }.Concat(group.Select(ModelRow)));
                rows.Items.Add(ChoiceRows(cloud, 13));
            }
            else
            {
                rows.Items.Add(ChoiceRows(section.Select(ModelRow), section.Key == "Built-in presets" ? 7 : 4));
            }
        }
    }

    private static ChoiceRow ModelRow(PanelChoice choice) =>
        new(choice, choice.Status.Length > 0 ? $"{choice.Name} · {choice.Status}" : choice.Name);

    private GridView ChoiceRows(IEnumerable<ChoiceRow> choices, int visibleRows)
    {
        var items = choices.ToArray();
        var grid = new GridView
        {
            ShowHeader = false,
            Border = BorderType.None,
            GridLines = GridLines.None,
            RowHeight = RowHeight,
            Height = Math.Min(items.Length, visibleRows) * RowHeight + 2,
            Width = 1,
            DataStore = items
        };
        grid.Columns.Add(new GridColumn
        {
            Expand = true,
            DataCell = new TextBoxCell { Binding = Binding.Property<ChoiceRow, string>(row => row.Label) }
        });
        grid.Columns.Add(new GridColumn
        {
            Width = 23,
            DataCell = new TextBoxCell
            {
                TextAlignment = TextAlignment.Center,
                Binding = Binding.Property<ChoiceRow, string>(row => CanMark(row) ? "✓" : "")
            }
        });
        grid.CellFormatting += (_, e) =>
        {
            if (e.Item is not ChoiceRow row)
                return;
            if (row.Choice is null || CanMark(row))
                e.Font = SystemFonts.Bold();
            if (CanMark(row))
                e.BackgroundColor = Color.Blend(SystemColors.ControlBackground, SystemColors.Highlight, 0.24f);
        };
        int current = Array.FindIndex(items, CanMark);
        if (current >= 0 && items[current].Choice is { } selected)
        {
            grid.SelectedRow = current;
            grid.ToolTip = ChoiceDetails(selected);
            if (current >= visibleRows)
                grid.Shown += (_, _) => grid.ScrollToRow(current);
        }
        grid.SelectionChanged += (_, _) => grid.ToolTip = grid.SelectedItem is ChoiceRow { Choice: { } choice }
            ? ChoiceDetails(choice) : null;
        grid.CellClick += (_, e) =>
        {
            if ((e.Buttons & MouseButtons.Primary) != 0)
                Choose(e.Item);
        };
        grid.KeyDown += (_, e) =>
        {
            if (e.Key == Keys.Enter)
            {
                Choose(grid.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Keys.Escape)
            {
                Dismiss();
                opener.Focus();
                e.Handled = true;
            }
        };
        return grid;
    }

    private bool CanMark(ChoiceRow row) => row.Choice is { } choice && choice.Key == selection?.Key;

    private void Choose(object? candidate)
    {
        if (!Enabled || !options.Visible || candidate is not ChoiceRow { Choice: { } choice } ||
            !choices.Any(available => available.Key == choice.Key))
            return;
        Dismiss();
        ChoiceSelected?.Invoke(choice.Key);
        opener.Focus();
    }

    private static string ChoiceDetails(PanelChoice choice)
    {
        if (choice.ModelId.Length == 0)
            return choice.Description.Length > 0 ? choice.Description : choice.Name;
        string detail = $"{choice.Name}\nModel ID · {choice.ModelId}";
        if (choice.Version.Length > 0 && choice.Version != choice.ModelId)
            detail += $"\nVersion · {choice.Version}";
        return detail;
    }
}

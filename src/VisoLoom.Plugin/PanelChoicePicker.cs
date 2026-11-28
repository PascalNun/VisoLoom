using Eto.Drawing;
using Eto.Forms;

namespace VisoLoom.Plugin;

internal sealed record PanelChoice(string Key, string Section, string Name, string Destination, string Status = "",
    string Subgroup = "", string Description = "", string ModelId = "", string Version = "");

internal sealed class PanelChoicePicker : StackLayout
{
    private const int RowHeight = 23;
    private const int CondensedProviderRows = 2;
    private readonly Button opener = new() { Width = 1 };
    private readonly WrappingLabel destination = VisoLoomPanel.CreateLabel("");
    private readonly StackLayout rows = SettingsLayout.Stack();
    private readonly List<GridView> lists = [];
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
        opener.Cursor = Cursors.Pointer;
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
            var active = lists.FirstOrDefault(list => list.SelectedRow >= 0) ?? lists.FirstOrDefault();
            if (active is not null)
                Application.Instance.AsyncInvoke(() =>
                {
                    if (options.Visible && lists.Contains(active))
                        active.Focus();
                });
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
        lists.Clear();
        foreach (var control in old)
            control.Dispose();

        foreach (var section in choices.GroupBy(choice => choice.Section))
        {
            if (rows.Items.Count > 0)
                rows.Items.Add(SettingsLayout.Rule());
            if (section.Key == "Diagnostics")
            {
                rows.Items.Add(new Expander { Header = section.Key, Expanded = false, Content = ChoiceRows(section.ToArray(), 1) });
                continue;
            }

            rows.Items.Add(VisoLoomPanel.CreateLabel(section.Key, bold: true));
            if (section.Key == "Cloud")
            {
                foreach (var provider in section.GroupBy(choice => choice.Subgroup))
                    rows.Items.Add(ProviderChoices(provider.Key, provider.ToArray()));
            }
            else
            {
                var grid = ChoiceRows(section.ToArray(), section.Key == "Built-in presets" ? 7 : 4);
                lists.Add(grid);
                rows.Items.Add(grid);
            }
        }
    }

    private Control ProviderChoices(string provider, PanelChoice[] all)
    {
        // Keep the provider and its current choice visible when a large
        // account catalogue is condensed. The full list scrolls under it.
        PanelChoice[] shortList = all.Take(CondensedProviderRows).ToArray();
        if (all.Length > CondensedProviderRows && all.FirstOrDefault(IsCurrent) is { } selected && !shortList.Contains(selected))
            shortList[^1] = selected;

        var grid = ChoiceRows(shortList, CondensedProviderRows);
        lists.Add(grid);
        var group = SettingsLayout.Stack(new Label { Text = provider, Font = SystemFonts.Bold() }, grid);
        group.Spacing = 2;
        if (all.Length <= CondensedProviderRows)
            return group;

        bool expanded = false;
        var more = new LinkButton { Text = $"Show all {all.Length} models", Cursor = Cursors.Pointer };
        more.ToolTip = $"Show all models from {provider}";
        more.Click += (_, _) =>
        {
            expanded = !expanded;
            SetRows(grid, expanded ? all : shortList, expanded ? 6 : CondensedProviderRows);
            more.Text = expanded ? "Show fewer models" : $"Show all {all.Length} models";
        };
        more.KeyDown += (_, e) => HandleEscape(e);
        group.Items.Add(SettingsLayout.Actions(more));
        return group;
    }

    private GridView ChoiceRows(PanelChoice[] choices, int visibleRows)
    {
        var grid = new GridView
        {
            ShowHeader = false,
            Border = BorderType.None,
            GridLines = GridLines.None,
            RowHeight = RowHeight,
            Width = 1
        };
        grid.Columns.Add(new GridColumn
        {
            Expand = true,
            DataCell = new TextBoxCell { Binding = Binding.Property<PanelChoice, string>(row =>
                row.Status.Length > 0 ? $"{row.Name} · {row.Status}" : row.Name) }
        });
        grid.Columns.Add(new GridColumn
        {
            Width = 23,
            DataCell = new TextBoxCell
            {
                TextAlignment = TextAlignment.Center,
                Binding = Binding.Property<PanelChoice, string>(row => IsCurrent(row) ? "✓" : "")
            }
        });
        int hoveredRow = -1;
        grid.CellFormatting += (_, e) =>
        {
            if (e.Item is not PanelChoice row)
                return;
            bool selected = IsCurrent(row);
            if (selected)
                e.Font = SystemFonts.Bold();
            if (selected || e.Row == hoveredRow)
            {
                e.ForegroundColor = SystemColors.ControlText;
                e.BackgroundColor = Color.Blend(SystemColors.ControlBackground, SystemColors.Highlight,
                    e.Row == hoveredRow ? 0.26f : 0.17f);
            }
        };
        grid.MouseMove += (_, e) => Hover(grid.GetCellAt(e.Location));
        grid.MouseLeave += (_, _) => Hover(null);
        grid.SelectionChanged += (_, _) =>
        {
            if (hoveredRow < 0)
                UpdateTooltip(grid, grid.SelectedItem as PanelChoice);
        };
        SetRows(grid, choices, visibleRows);
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
            else
                HandleEscape(e);
        };
        return grid;

        void Hover(GridCell? cell)
        {
            int next = cell?.RowIndex ?? -1;
            if (hoveredRow == next)
                return;
            int previous = hoveredRow;
            hoveredRow = next;
            int count = (grid.DataStore as PanelChoice[])?.Length ?? 0;
            if (previous >= 0 && previous < count) grid.ReloadData(previous);
            if (next >= 0 && next < count) grid.ReloadData(next);
            grid.Cursor = next >= 0 ? Cursors.Pointer : Cursors.Default;
            UpdateTooltip(grid, next >= 0 ? cell?.Item as PanelChoice : grid.SelectedItem as PanelChoice);
        }
    }

    private void SetRows(GridView grid, PanelChoice[] items, int visibleRows)
    {
        grid.Height = Math.Min(items.Length, visibleRows) * RowHeight + 2;
        grid.DataStore = items;
        int index = Array.FindIndex(items, IsCurrent);
        grid.SelectedRow = index;
        UpdateTooltip(grid, index < 0 ? null : items[index]);
    }

    private static void UpdateTooltip(GridView grid, PanelChoice? choice) =>
        grid.ToolTip = choice is null ? null : ChoiceDetails(choice);

    private bool IsCurrent(PanelChoice choice) => choice.Key == selection?.Key;

    private void HandleEscape(KeyEventArgs e)
    {
        if (e.Key != Keys.Escape)
            return;
        Dismiss();
        opener.Focus();
        e.Handled = true;
    }

    private void Choose(object? candidate)
    {
        if (!Enabled || !options.Visible || candidate is not PanelChoice choice ||
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

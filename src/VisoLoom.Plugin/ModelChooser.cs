using Eto.Drawing;
using Eto.Forms;

namespace VisoLoom.Plugin;

internal sealed class ModelChooser : Dialog<string?>
{
    private readonly ListBox models = new() { Width = 1 };
    private readonly WrappingLabel details = VisoLoomPanel.CreateLabel("");
    private readonly Button choose = new() { Text = "Choose model", Enabled = false };
    private IReadOnlyList<PanelChoice> shown = [];
    private string? chosen;

    public ModelChooser(IReadOnlyList<PanelChoice> choices, PanelChoice? current)
    {
        Title = "VisoLoom · Choose model";
        ClientSize = new Size(530, 360);
        MinimumSize = new Size(420, 290);
        Resizable = true;

        var groups = SettingsLayout.Stack();
        groups.Padding = new Padding(0, 0, 8, 0);
        groups.Width = 150;
        (RadioButton Button, PanelChoice[] Entries)? first = null;
        (RadioButton Button, PanelChoice[] Entries)? initial = null;
        foreach (var section in choices.GroupBy(choice => choice.Section))
        {
            if (first is not null)
                groups.Items.Add(SettingsLayout.Rule());
            if (section.Key == "Cloud")
                groups.Items.Add(VisoLoomPanel.CreateLabel("Cloud", bold: true));

            foreach (var group in section.GroupBy(choice => choice.Subgroup))
            {
                var button = first is { } controller ? new RadioButton(controller.Button) : new RadioButton();
                button.Text = group.Key.Length > 0 ? group.Key : section.Key;
                var entries = group.ToArray();
                first ??= (button, entries);
                button.CheckedChanged += (_, _) =>
                {
                    if (button.Checked)
                        ShowModels(entries, current);
                };
                groups.Items.Add(button);

                if (entries.Any(entry => current is not null &&
                    (entry.Key == current.Key || (current.Key.Length == 0 && entry.Destination == current.Destination))))
                    initial ??= (button, entries);
            }
        }

        var list = new DynamicLayout { Spacing = new Size(0, 5) };
        list.Add(VisoLoomPanel.CreateLabel("Models", bold: true), xscale: true);
        list.Add(models, xscale: true, yscale: true);

        var cancel = new Button { Text = "Cancel" };
        var layout = new DynamicLayout { Padding = 12, Spacing = new Size(8, 8) };
        layout.Add(new TableLayout
        {
            Spacing = new Size(8, 0),
            Rows = { new TableRow(groups, new TableCell(list, scaleWidth: true)) { ScaleHeight = true } }
        }, xscale: true, yscale: true);
        layout.AddRow(details);
        layout.AddRow(new TableLayout { Spacing = new Size(6, 0), Rows = { new TableRow(null, cancel, choose) } });
        Content = layout;
        DefaultButton = choose;
        AbortButton = cancel;

        models.SelectedIndexChanged += (_, _) => ShowDetails(current);
        models.Activated += (_, _) => Choose();
        choose.Click += (_, _) => Choose();
        cancel.Click += (_, _) => Close(null);
        Shown += (_, _) =>
        {
            if ((initial ?? first) is not { } active)
                return;
            active.Button.Checked = true;
            if (shown != active.Entries)
                ShowModels(active.Entries, current);
            if (models.SelectedIndex >= 0)
                models.Focus();
            else
                active.Button.Focus();
        };
    }

    private PanelChoice? Selected => shown.FirstOrDefault(choice => choice.Key == models.SelectedKey);

    private void ShowModels(IReadOnlyList<PanelChoice> choices, PanelChoice? current)
    {
        shown = choices;
        models.Items.Clear();
        foreach (var choice in shown)
        {
            string label = choice.Status.Length == 0 ? choice.Name : $"{choice.Name} · {choice.Status}";
            models.Items.Add(new ListItem { Key = choice.Key, Text = choice.Key == current?.Key ? $"✓  {label}" : label });
        }
        models.SelectedIndex = -1;
        if (current is not null && shown.Any(choice => choice.Key == current.Key))
            models.SelectedKey = current.Key;
        ShowDetails(current);
    }

    private void ShowDetails(PanelChoice? current)
    {
        var model = Selected;
        choose.Enabled = model is not null;
        if (model is null)
        {
            details.Text = current is { Status: "Unavailable" } ? $"Saved model unavailable · {current.Name}" : "Select a model";
            return;
        }

        details.Text = model.ModelId.Length > 0 ? $"Model ID · {model.ModelId}" : model.Destination;
        if (model.Version.Length > 0 && model.Version != model.ModelId)
            details.Text += $"\nVersion · {model.Version}";
    }

    private void Choose()
    {
        if (chosen is not null || Selected is not { } model)
            return;
        chosen = model.Key;
        Close(chosen);
    }
}

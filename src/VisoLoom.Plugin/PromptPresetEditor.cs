using Eto.Drawing;
using Eto.Forms;
using VisoLoom.Core;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

// Edit preset… / Save as new preset… for representation presets (information
// architecture, "Prompt presets"; ADR 0029). The selected preset's name,
// description and representation instructions are exposed and editable. The
// supplied presets stay exactly as shipped: saving from one creates a named
// personal version that keeps the supplied starting point in the list. A
// personal preset lives in the user's application data, outside `.3dm` and
// Git, is deleted only with an explicit confirmation, and instructs only
// future submissions — a running job keeps the intent it already recorded.
// The shared geometry-preservation objective is composed into every prompt
// regardless of what a preset says, so the editable fields can only extend it.
internal static class PromptPresetEditor
{
    private static readonly PersonalPresetStore Store = new();

    public static void Show(Control owner, CaptureCoordinator session, Action<string?> refreshSelection)
    {
        RepresentationPreset source = session.SelectedPreset;
        bool personal = PromptPresets.IsPersonal(source);

        var name = new TextBox { Text = personal ? source.Name : "", Width = 1 };
        var description = new TextBox { Text = source.Description, Width = 1 };
        var instruction = new TextArea { Text = source.BaseInstruction, Height = 150, Width = 1, Wrap = true };
        var preservation = new TextArea { Text = source.PreservationIntent, Height = 90, Width = 1, Wrap = true };
        var vegetation = new TextArea { Text = source.VegetationPolicy, Height = 60, Width = 1, Wrap = true };
        var save = new Button { Text = "Save preset", Visible = personal };
        var saveAsNew = new Button { Text = "Save as new preset…" };
        var remove = new Button { Text = "Delete preset…", Visible = personal };
        var close = new Button { Text = "Close" };
        var error = VisoLoomPanel.CreateLabel("");

        using var dialog = new Dialog { Title = "VisoLoom · Edit preset", ClientSize = new Size(420, 560), MinimumSize = new Size(380, 460), Resizable = true };
        var layout = new DynamicLayout { Padding = 12, Spacing = new Size(8, 8) };
        layout.AddRow(VisoLoomPanel.CreateLabel(personal ? "Personal preset" : "Supplied preset", bold: true));
        layout.AddRow(VisoLoomPanel.CreateLabel(personal ?
            "Saved in your user settings, outside this Rhino file. Save preset updates it; Save as new preset… keeps this one and adds a copy." :
            "Saving creates a personal version in your user settings. The supplied preset stays unchanged in the list."));
        layout.AddRow(VisoLoomPanel.CreateLabel("Name"));
        layout.AddRow(name);
        layout.AddRow(VisoLoomPanel.CreateLabel("Short description", bold: false));
        layout.AddRow(description);
        layout.AddRow(VisoLoomPanel.CreateLabel("Representation instructions"));
        layout.AddRow(instruction);
        var detail = new DynamicLayout { Padding = new Padding(8, 4), Spacing = new Size(6, 6) };
        detail.AddRow(VisoLoomPanel.CreateLabel("Preservation intent"));
        detail.AddRow(preservation);
        detail.AddRow(VisoLoomPanel.CreateLabel("Vegetation policy"));
        detail.AddRow(vegetation);
        detail.AddRow(VisoLoomPanel.CreateLabel("The geometry-preservation objective is always added to the composed prompt; these fields extend it."));
        layout.AddRow(new Expander { Header = "Preservation detail", Expanded = false, Content = detail });
        var actions = new DynamicLayout { Spacing = new Size(8, 6) };
        actions.AddRow(save);
        actions.AddRow(saveAsNew);
        actions.AddRow(remove);
        actions.AddRow(close);
        layout.Add(actions);
        layout.AddRow(error);
        dialog.Content = layout;

        save.Click += (_, _) =>
        {
            if (Edited(out var edited) && NoNameConflict(edited.Name, source.Id))
                if (Persist(edited with { Id = source.Id }, error, out var stored))
                {
                    refreshSelection(stored.Id);
                    dialog.Close();
                }
        };
        saveAsNew.Click += (_, _) =>
        {
            // A new entry never owns its name yet, not even when it copies a
            // personal preset; two presets with the same name would confuse the dropdown.
            if (Edited(out var edited) && NoNameConflict(edited.Name, null))
                if (Persist(edited with { Id = PromptPresets.UniquePersonalId(edited.Name) }, error, out var stored))
                {
                    refreshSelection(stored.Id);
                    dialog.Close();
                }
        };
        remove.Click += (_, _) => ConfirmDelete(dialog, source, error, () =>
        {
            refreshSelection(null);
            dialog.Close();
        });
        close.Click += (_, _) => dialog.Close();
        if (PromptPresets.Problem is { Length: > 0 } problem)
            error.Text = problem;
        dialog.ShowModal(owner);

        bool Edited(out RepresentationPreset edited)
        {
            edited = Build(source, name.Text, description.Text, instruction.Text, preservation.Text, vegetation.Text);
            if (edited.Name.Length == 0) { error.Text = "Name the preset before saving."; return false; }
            if (edited.Name.Length > 100) { error.Text = "The name must stay within 100 characters."; return false; }
            if (edited.Description.Length > 400) { error.Text = "The short description must stay within 400 characters."; return false; }
            if (edited.BaseInstruction.Length == 0) { error.Text = "The preset needs representation instructions."; return false; }
            if (edited.BaseInstruction.Length > 16_000) { error.Text = "The instructions must stay within 16,000 characters."; return false; }
            return true;
        }

        bool NoNameConflict(string candidate, string? ownId)
        {
            if (NameConflict(candidate, ownId) is not { } conflict) return true;
            error.Text = conflict;
            return false;
        }
    }

    // Deleting a personal preset is confirmed exactly once, in place, with the
    // consequence named; the supplied presets cannot be deleted.
    private static void ConfirmDelete(Dialog owner, RepresentationPreset source, WrappingLabel error, Action deleted)
    {
        using var confirm = new Dialog { Title = "VisoLoom · Delete preset", ClientSize = new Size(360, 170), Resizable = false };
        var delete = new Button { Text = "Delete preset" };
        var cancel = new Button { Text = "Cancel" };
        var layout = new DynamicLayout { Padding = 12, Spacing = new Size(8, 8) };
        layout.AddRow(VisoLoomPanel.CreateLabel("Delete \"" + source.Name + "\"? This cannot be undone.", bold: true));
        layout.AddRow(VisoLoomPanel.CreateLabel("Only this personal preset is removed; the supplied presets stay available."));
        layout.AddRow(new TableLayout { Rows = { new TableRow(delete, null, cancel) }, Spacing = new Size(8, 0) });
        confirm.Content = layout;
        delete.Click += (_, _) =>
        {
            try
            {
                Store.Delete(source.Id);
            }
            catch (ArgumentException ex) { error.Text = ex.Message; confirm.Close(); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            { error.Text = "The personal preset could not be deleted. Check the VisoLoom presets folder in your application data."; confirm.Close(); return; }
            confirm.Close();
            deleted();
        };
        cancel.Click += (_, _) => confirm.Close();
        confirm.ShowModal(owner);
    }

    private static RepresentationPreset Build(RepresentationPreset source, string name, string description,
        string instruction, string preservation, string vegetation) => source with
    {
        Name = name.Trim(),
        Description = description.Trim(),
        BaseInstruction = instruction.Trim(),
        PreservationIntent = preservation.Trim(),
        VegetationPolicy = vegetation.Trim()
    };

    private static string? NameConflict(string name, string? ownId) =>
        PromptPresets.All.Any(choice => choice.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && choice.Id != ownId)
            ? "A preset with this name already exists." : null;

    private static bool Persist(RepresentationPreset preset, WrappingLabel error, out RepresentationPreset stored)
    {
        try
        {
            stored = Store.Save(preset);
            return true;
        }
        catch (ArgumentException ex)
        { error.Text = ex.Message; stored = preset; return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { error.Text = "The personal preset could not be saved. Check the VisoLoom presets folder in your application data."; stored = preset; return false; }
    }
}

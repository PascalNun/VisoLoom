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
        RepresentationPreset? source = session.SelectedPreset;
        var empty = new RepresentationPreset { Id = "", Name = "", BaseInstruction = "", PreservationIntent = "", VegetationPolicy = "" };
        var baseline = empty;
        var name = new TextBox { Width = 1 };
        var description = new TextBox { Width = 1 };
        var instruction = new TextArea { Height = 150, Width = 1, Wrap = true };
        var preservation = new TextArea { Height = 90, Width = 1, Wrap = true };
        var vegetation = new TextArea { Height = 60, Width = 1, Wrap = true };
        var heading = VisoLoomPanel.CreateLabel("", bold: true);
        var help = VisoLoomPanel.CreateLabel("");
        var newPreset = new Button { Text = "New preset" };
        var reset = new Button { Text = "Reset changes", Enabled = false };
        var save = new Button { Text = "Save preset" };
        var saveAsNew = new Button { Text = "Save as new preset…" };
        var remove = new Button { Text = "Delete preset…" };
        var close = new Button { Text = "Cancel" };
        var error = VisoLoomPanel.CreateLabel("");

        using var dialog = new Dialog
        {
            Title = "VisoLoom · Edit preset", ClientSize = new Size(450, 590),
            MinimumSize = new Size(380, 445), Resizable = true, AbortButton = close
        };
        var form = new DynamicLayout { Spacing = new Size(8, 8) };
        form.AddRow(SettingsLayout.Row(heading, newPreset));
        form.AddRow(help);
        form.AddRow(VisoLoomPanel.CreateLabel("Name"));
        form.AddRow(name);
        form.AddRow(VisoLoomPanel.CreateLabel("Short description"));
        form.AddRow(description);
        form.AddRow(VisoLoomPanel.CreateLabel("Representation instructions"));
        form.AddRow(instruction);
        var detail = new DynamicLayout { Padding = new Padding(8, 4), Spacing = new Size(6, 6) };
        detail.AddRow(VisoLoomPanel.CreateLabel("Preservation intent"));
        detail.AddRow(preservation);
        detail.AddRow(VisoLoomPanel.CreateLabel("Vegetation policy"));
        detail.AddRow(vegetation);
        detail.AddRow(VisoLoomPanel.CreateLabel("Every render includes the shared geometry-preservation instructions."));
        form.AddRow(new Expander { Header = "Preservation detail", Expanded = false, Content = detail });
        var layout = new DynamicLayout { Padding = 12, Spacing = new Size(6, 8) };
        layout.Add(new Scrollable { Content = form, Border = BorderType.None, ExpandContentWidth = true }, xscale: true, yscale: true);
        layout.AddRow(error);
        layout.AddRow(new TableLayout { Spacing = new Size(6, 0), Rows = { new TableRow(reset, remove, null, close) } });
        layout.AddRow(new TableLayout { Spacing = new Size(6, 0), Rows = { new TableRow(null, save, saveAsNew) } });
        dialog.Content = layout;

        newPreset.Click += (_, _) =>
        {
            if (reset.Enabled && MessageBox.Show(dialog, "Discard the current unsaved changes and start a blank preset?",
                    "New preset", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;
            LoadDraft(null);
            name.Focus();
        };
        reset.Click += (_, _) => RestoreDraft();
        name.TextChanged += (_, _) => UpdateReset();
        description.TextChanged += (_, _) => UpdateReset();
        instruction.TextChanged += (_, _) => UpdateReset();
        preservation.TextChanged += (_, _) => UpdateReset();
        vegetation.TextChanged += (_, _) => UpdateReset();
        save.Click += (_, _) =>
        {
            if (source is { } original && PromptPresets.IsPersonal(original))
                Save(original.Id);
        };
        saveAsNew.Click += (_, _) => Save(null);
        remove.Click += (_, _) =>
        {
            if (source is { } original && PromptPresets.IsPersonal(original))
            {
                ConfirmDelete(dialog, original, error, () =>
                {
                    refreshSelection(null);
                    dialog.Close();
                });
                error.Visible = error.Text.Length > 0;
            }
        };
        close.Click += (_, _) => dialog.Close();

        LoadDraft(source);
        dialog.ShowModal(owner);

        void LoadDraft(RepresentationPreset? choice)
        {
            source = choice;
            bool personal = choice is not null && PromptPresets.IsPersonal(choice);
            baseline = choice is null ? empty : choice with { Name = personal ? choice.Name : "" };
            heading.Text = choice is null ? "New representation preset" : personal ? "Personal preset" : choice.Name;
            help.Text = choice is null ? "Add a name and describe the intended representation."
                : personal ? "Saved in your presets." : "Edit the instructions and save a personal copy.";
            newPreset.Visible = choice is not null;
            save.Visible = remove.Visible = personal;
            saveAsNew.Text = choice is null ? "Add preset" : "Save as new preset…";
            RestoreDraft();
        }

        void RestoreDraft()
        {
            name.Text = baseline.Name;
            description.Text = baseline.Description;
            instruction.Text = baseline.BaseInstruction;
            preservation.Text = baseline.PreservationIntent;
            vegetation.Text = baseline.VegetationPolicy;
            reset.Enabled = false;
            error.Text = PromptPresets.Problem ?? "";
            error.Visible = error.Text.Length > 0;
        }

        void UpdateReset()
        {
            reset.Enabled = name.Text != baseline.Name || description.Text != baseline.Description ||
                instruction.Text != baseline.BaseInstruction || preservation.Text != baseline.PreservationIntent ||
                vegetation.Text != baseline.VegetationPolicy;
        }

        void Save(string? existingId)
        {
            error.Visible = true;
            if (!Edited(out var edited) || !NoNameConflict(edited.Name, existingId))
                return;
            var selected = edited with { Id = existingId ?? PromptPresets.UniquePersonalId(edited.Name) };
            if (Persist(selected, error, out var stored))
            {
                refreshSelection(stored.Id);
                dialog.Close();
            }
        }

        bool Edited(out RepresentationPreset edited)
        {
            edited = Build(baseline, name.Text, description.Text, instruction.Text, preservation.Text, vegetation.Text);
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

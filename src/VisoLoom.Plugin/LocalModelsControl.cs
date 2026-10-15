using Eto.Drawing;
using Eto.Forms;
using System.Text.Json;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal sealed class LocalModelsControl : Panel
{
    private readonly LocalModelLibrary library = new();
    private readonly ListBox folders = new() { Height = 100, Width = 1 };
    private readonly WrappingLabel details = VisoLoomPanel.CreateLabel("Browse to a model folder to check its format. No files are copied and no image is generated.");
    private readonly Button browse = new() { Text = "Find existing model…" };
    private readonly Button refresh = new() { Text = "Refresh library" };
    private readonly Button remove = new() { Text = "Remove from list" };
    private readonly CancellationToken lifetime;
    private IReadOnlyList<LocalModelFolder> models = [];
    private bool busy;

    public LocalModelsControl(CancellationToken lifetime)
    {
        this.lifetime = lifetime;
        var layout = new DynamicLayout { Spacing = new Size(8, 8) };
        layout.AddRow(VisoLoomPanel.CreateLabel("Existing model folders", bold: true));
        layout.AddRow(VisoLoomPanel.CreateLabel("Reuse starts with a format check. Saving a folder here does not switch the rendering model."));
        layout.AddRow(folders);
        layout.AddRow(details);
        layout.AddRow(browse);
        layout.AddRow(refresh, remove);
        Content = layout;
        folders.SelectedIndexChanged += (_, _) => ShowSelection();
        refresh.Click += async (_, _) => await RunAsync(RefreshAsync);
        browse.Click += async (_, _) =>
        {
            using var dialog = new SelectFolderDialog { Title = "Choose the model's own folder" };
            if (dialog.ShowDialog(this) != DialogResult.Ok) return;
            await RunAsync(async () =>
            {
                var report = await library.InspectAsync(dialog.Directory, lifetime);
                lifetime.ThrowIfCancellationRequested();
                if (!report.Candidate) { details.Text = report.Message; return; }
                await library.AddAsync(report.Directory, lifetime);
                await RefreshAsync(report.Id);
            });
        };
        remove.Click += async (_, _) =>
        {
            if (Selected is not { Configured: false } selected) return;
            await RunAsync(async () => { await library.RemoveAsync(selected.Id, lifetime); await RefreshAsync(); });
        };
        Load += async (_, _) => await RunAsync(RefreshAsync);
        ShowSelection();
    }

    private LocalModelFolder? Selected => folders.SelectedIndex >= 0 && folders.SelectedIndex < models.Count ? models[folders.SelectedIndex] : null;

    public static void ShowLibrary(Control owner)
    {
        using var lifetime = new CancellationTokenSource();
        using var dialog = new Dialog { Title = "VisoLoom · Model library", ClientSize = new Size(460, 540), Resizable = true };
        var done = new Button { Text = "Done" };
        var layout = new DynamicLayout { Padding = 12, Spacing = new Size(8, 8) };
        layout.Add(new Scrollable { Content = new LocalModelsControl(lifetime.Token), Border = BorderType.None, ExpandContentWidth = true }, yscale: true);
        layout.AddRow(done);
        dialog.Content = layout;
        dialog.AbortButton = done;
        done.Click += (_, _) => dialog.Close();
        try { dialog.ShowModal(owner); }
        finally { lifetime.Cancel(); }
    }

    private Task RefreshAsync() => RefreshAsync(null);
    private async Task RefreshAsync(string? selected)
    {
        var updated = await library.ListAsync(lifetime);
        lifetime.ThrowIfCancellationRequested();
        models = updated;
        folders.Items.Clear();
        foreach (var model in models)
            folders.Items.Add(new ListItem { Text = model.Name + (model.Configured ? model.Readiness == "ready" ? " · ready" : " · configured; check needed" : " · saved for setup") });
        folders.SelectedIndex = selected is null ? (models.Count == 0 ? -1 : 0) : models.ToList().FindIndex(m => m.Id == selected);
        if (models.Count == 0) details.Text = "No model folders saved. Use Find existing model… to check one. Nothing will be downloaded.";
        ShowSelection();
    }

    private void ShowSelection()
    {
        remove.Enabled = !busy && Selected is { Configured: false };
        if (Selected is not { } model) return;
        details.Text = model.Directory + "\n" + model.FileCount + " files · " + (model.Bytes / 1_000_000_000d).ToString("0.##") + " GB\n" +
            (model.Configured ? (model.ReadinessMessage ?? "Current development connection. Use Check local model above to confirm rendering readiness.") + "\n" : "Saved for setup. Preparation and activation are separate steps.\n") +
            (model.Readiness == "ready" ? "Rendering uses the existing verified copy. Finding another folder does not switch models." : model.Message);
    }

    private async Task RunAsync(Func<Task> work)
    {
        if (busy || lifetime.IsCancellationRequested) return;
        busy = true;
        browse.Enabled = refresh.Enabled = remove.Enabled = folders.Enabled = false;
        details.Text = "Checking local model folders; no weights are loaded…";
        try { await work(); }
        catch (ProviderException ex) { if (!lifetime.IsCancellationRequested) details.Text = ex.Message; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { if (!lifetime.IsCancellationRequested) details.Text = "Could not read the local model library. Check the selected folder and development runtime."; }
        catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) details.Text = "The folder check stopped after its time limit. Choose a model folder on an available local drive."; }
        finally
        {
            busy = false;
            if (!lifetime.IsCancellationRequested)
            {
                browse.Enabled = refresh.Enabled = folders.Enabled = true;
                remove.Enabled = Selected is { Configured: false };
            }
        }
    }
}

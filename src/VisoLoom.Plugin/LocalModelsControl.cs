using Eto.Drawing;
using Eto.Forms;
using Rhino;
using System.Text.Json;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal sealed class LocalModelsControl : Panel
{
    private readonly LocalModelLibrary library = new();
    private readonly ListBox catalogue = new() { Height = 72, Width = 1 };
    private readonly Panel localFolders = new();
    private readonly WrappingLabel choice = VisoLoomPanel.CreateLabel("");
    private readonly Button configureCloud = new() { Text = "Set up OpenAI…" };
    private readonly Button configureLocal = new() { Text = "Check local connection…" };
    private readonly Button plan = new() { Text = "Check setup" };
    private readonly WrappingLabel requirements = VisoLoomPanel.CreateLabel("");
    private readonly WrappingLabel setupDetails = VisoLoomPanel.CreateLabel("");
    private readonly Expander folderDetails;
    private readonly ListBox folders = new() { Height = 100, Width = 1 };
    private readonly WrappingLabel details = VisoLoomPanel.CreateLabel("Browse to a model folder to check its format. No files are copied and no image is generated.");
    private readonly Button browse = new() { Text = "Find existing model…" };
    private readonly Button refresh = new() { Text = "Refresh model files" };
    private readonly Button remove = new() { Text = "Remove from list" };
    private readonly CancellationToken lifetime;
    private readonly bool hasLocalSettings;
    // The active document's coordinator pairs the installed inventory with the
    // running engine's own capabilities whenever a connection check completes.
    private readonly CaptureCoordinator? coordinator;
    private bool updatingCatalogue;
    private IReadOnlyList<LocalModelFolder> models = [];
    private bool busy;
    private bool loaded;

    public LocalModelsControl(CancellationToken lifetime, Action openCloudSettings, Action? openLocalSettings = null)
    {
        this.lifetime = lifetime;
        hasLocalSettings = openLocalSettings is not null;
        var folderLayout = new DynamicLayout { Spacing = new Size(8, 8) };
        folderLayout.AddRow(VisoLoomPanel.CreateLabel("Find and check model files already on this computer."));
        folderLayout.AddRow(folders);
        folderLayout.AddRow(details);
        folderLayout.AddRow(new Expander { Header = "Setup details", Expanded = false, Content = setupDetails });
        // Separate button rows avoid sharing minimum column widths with the list.
        folderLayout.AddRow(browse);
        folderLayout.AddRow(plan);
        folderLayout.AddRow(refresh);
        folderLayout.AddRow(remove);
        localFolders.Content = folderLayout;
        folderDetails = new Expander { Header = "Existing model files", Expanded = false, Content = localFolders };
        var layout = new DynamicLayout { Spacing = new Size(8, 8) };
        layout.AddRow(VisoLoomPanel.CreateLabel("Model connections", bold: true));
        layout.AddRow(VisoLoomPanel.CreateLabel("Set up a connection here, then select the model in the Render panel."));
        layout.AddRow(catalogue);
        layout.AddRow(choice);
        layout.AddRow(configureCloud);
        layout.AddRow(configureLocal);
        layout.AddRow(requirements);
        layout.AddRow(folderDetails);
        Content = layout;
        foreach (var _ in ModelCatalogue.Choices)
            catalogue.Items.Add(new ListItem());
        if (RhinoDoc.ActiveDoc is { } active)
        {
            coordinator = CaptureCoordinator.ForDocument(active.RuntimeSerialNumber);
            coordinator.Changed += CoordinatorChanged;
        }
        UpdateCatalogue();
        catalogue.SelectedIndexChanged += (_, _) => ShowCatalogue();
        configureCloud.Click += (_, _) => openCloudSettings();
        configureLocal.Visible = openLocalSettings is not null;
        configureLocal.Click += (_, _) => openLocalSettings?.Invoke();
        catalogue.SelectedIndex = 0;
        ShowCatalogue();
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
        plan.Click += async (_, _) =>
        {
            if (Selected is not { } selected) return;
            await RunAsync(async () =>
            {
                var report = await library.PlanAsync(selected.Id, lifetime);
                lifetime.ThrowIfCancellationRequested();
                details.Text = report.State == "connectionCheckNeeded" ? "Files verified. Check the connection before rendering." : report.Message;
                setupDetails.Text = selected.Directory + "\n\n" +
                    string.Join("\n\n", report.Steps.Select(step => step.Name + ": " + step.Message)) +
                    (report.PreparationFreeBytes is { } free ? "\n\nManaged-copy volume: " +
                        (free / (double)(1L << 30)).ToString("0.#") + " GiB free; preparation needs at least " +
                        (report.MinimumPreparationFreeBytes / (double)(1L << 30)).ToString("0.#") + " GiB." : "");
            });
        };
        remove.Click += async (_, _) =>
        {
            if (Selected is not { Configured: false } selected) return;
            await RunAsync(async () => { await library.RemoveAsync(selected.Id, lifetime); await RefreshAsync(); });
        };
        Load += async (_, _) =>
        {
            if (loaded) return;
            loaded = true;
            await RunAsync(RefreshAsync);
        };
        ShowSelection();
    }

    private LocalModelFolder? Selected => folders.SelectedIndex >= 0 && folders.SelectedIndex < models.Count ? models[folders.SelectedIndex] : null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && coordinator is { } session)
            session.Changed -= CoordinatorChanged;
        base.Dispose(disposing);
    }

    // Called on the UI thread whenever a connection check or capture changes the
    // document's session; keep the setup list paired with the live engine.
    private void CoordinatorChanged(object? sender, EventArgs e)
    {
        if (lifetime.IsCancellationRequested) return;
        UpdateCatalogue();
        ShowCatalogue();
    }

    private void UpdateCatalogue()
    {
        if (updatingCatalogue) return;
        updatingCatalogue = true;
        try
        {
            string? running = coordinator?.LocalModel?.Id;
            for (int i = 0; i < ModelCatalogue.Choices.Count; i++)
            {
                var entry = ModelCatalogue.Choices[i];
                catalogue.Items[i].Text = entry.Name + " · " + entry.Location +
                    (running is not null && entry.EngineProfileId == running ? " · connected" : "");
            }
        }
        finally { updatingCatalogue = false; }
    }

    private void ShowCatalogue()
    {
        int index = catalogue.SelectedIndex;
        if (index < 0 || index >= ModelCatalogue.Choices.Count) return;
        var item = ModelCatalogue.Choices[index];
        bool local = item.Id != "openai";
        localFolders.Visible = folderDetails.Visible = local;
        configureCloud.Visible = !local;
        configureLocal.Visible = local && hasLocalSettings;
        choice.Text = item.Strengths;
        requirements.Text = "Requirements · " + item.Hardware + "\n\nStorage · " + item.Storage + "\n\n" + item.Setup;
        if (EngineState(item) is { } state)
            requirements.Text += "\n\n" + state;
    }

    // The engine's own capability report is the authority on which reviewed
    // profile is actually in service; nothing here infers readiness from files.
    private string? EngineState(ModelCatalogueEntry item)
    {
        if (item.EngineProfileId is not { } profile || coordinator?.LocalModel is not { } running)
            return null;
        return running.Id == profile
            ? "Connected. The engine serves this profile and reports model version " + running.Version + ". Choose it in the Render panel."
            : "The connected engine is serving " + running.DisplayName + ". " + item.Name + " is not active in this engine session.";
    }

    private Task RefreshAsync() => RefreshAsync(null);
    private async Task RefreshAsync(string? selected)
    {
        var updated = await library.ListAsync(lifetime);
        lifetime.ThrowIfCancellationRequested();
        models = updated;
        folders.Items.Clear();
        foreach (var model in models)
            folders.Items.Add(new ListItem { Text = model.Name + (model.Configured ? model.Readiness == "ready" ? " · files verified" : " · needs checking" : " · setup needed") });
        folders.SelectedIndex = selected is null ? (models.Count == 0 ? -1 : 0) : models.ToList().FindIndex(m => m.Id == selected);
        if (models.Count == 0) details.Text = "No model folders saved. Use Find existing model… to check one. Nothing will be downloaded.";
        ShowSelection();
    }

    private void ShowSelection()
    {
        plan.Enabled = !busy && Selected is not null;
        remove.Enabled = !busy && Selected is { Configured: false };
        if (Selected is not { } model) { setupDetails.Text = ""; return; }
        details.Text = model.FileCount + " files · " + (model.Bytes / 1_000_000_000d).ToString("0.##") + " GB\n" +
            (model.Configured && model.Readiness == "ready" ? "Files verified. Check the connection before rendering." : "Setup is needed. Check setup to see the next steps.");
        setupDetails.Text = model.Directory + "\n\n" + (model.ReadinessMessage ?? model.Message);
    }

    private async Task RunAsync(Func<Task> work)
    {
        if (busy || lifetime.IsCancellationRequested) return;
        busy = true;
        plan.Enabled = false;
        browse.Enabled = refresh.Enabled = remove.Enabled = folders.Enabled = false;
        details.Text = "Checking local model folders; no weights are loaded…";
        try { await work(); }
        catch (ProviderException ex) { if (!lifetime.IsCancellationRequested) details.Text = ex.Message; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { if (!lifetime.IsCancellationRequested) details.Text = "Could not read the model files. Check the folder and local setup in Connections."; }
        catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) details.Text = "The folder check stopped after its time limit. Choose a model folder on an available local drive."; }
        finally
        {
            busy = false;
            if (!lifetime.IsCancellationRequested)
            {
                browse.Enabled = refresh.Enabled = folders.Enabled = true;
                plan.Enabled = Selected is not null;
                remove.Enabled = Selected is { Configured: false };
            }
        }
    }
}

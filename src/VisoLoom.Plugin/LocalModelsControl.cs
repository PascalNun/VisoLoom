using Eto.Drawing;
using Eto.Forms;
using Rhino;
using System.Text.Json;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal sealed class LocalModelsControl : Panel
{
    private readonly LocalModelLibrary library = new();
    private readonly LocalModelInstaller installer = new();
    private readonly ListBox catalogue = new() { Height = 72, Width = 1 };
    private readonly Panel localFolders = new();
    private readonly Expander downloads;
    private readonly ListBox downloadList = new() { Height = 72, Width = 1 };
    private readonly WrappingLabel packageInfo = VisoLoomPanel.CreateLabel("");
    private readonly WrappingLabel locationLabel = VisoLoomPanel.CreateLabel("");
    private readonly WrappingLabel installDetails = VisoLoomPanel.CreateLabel("");
    private readonly Button installModel = new() { Text = "Install model…" };
    private readonly Button changeLocation = new() { Text = "Change download location…" };
    private readonly Button cancelDownload = new() { Text = "Cancel download", Visible = false };
    private readonly Button deleteDownload = new() { Text = "Delete downloaded files…" };
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
    private readonly Button remove = new() { Text = "Forget location" };
    private readonly CancellationToken lifetime;
    private readonly bool hasLocalSettings;
    // The active document's coordinator pairs the installed inventory with the
    // running engine's own capabilities whenever a connection check completes.
    private readonly CaptureCoordinator? coordinator;
    private bool updatingCatalogue;
    private IReadOnlyList<LocalModelFolder> models = [];
    private IReadOnlyList<ReviewedModelPackage> packages = [];
    private readonly Dictionary<string, string> chosenLocations = new();
    private readonly HashSet<string> managedDestinations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> installedDestinations = new(StringComparer.Ordinal);
    private CancellationTokenSource? downloadSource;
    private bool busy;
    private bool loaded;
    private bool downloadToolsAvailable = true;

    public LocalModelsControl(CancellationToken lifetime, Action<string> openCloudSettings, Action? openLocalSettings = null)
    {
        this.lifetime = lifetime;
        hasLocalSettings = openLocalSettings is not null;
        // Reviewed downloads: one click, verified files, the truthful status.
        // The plan check runs before the first byte; a download reports per file
        // and stops cleanly, leaving the engine's staging cleanup to the engine.
        var downloadLayout = new DynamicLayout { Spacing = new Size(8, 8) };
        downloadLayout.AddRow(VisoLoomPanel.CreateLabel("Review the package, check the space, then download verified files from their pinned sources."));
        downloadLayout.AddRow(downloadList);
        downloadLayout.AddRow(packageInfo);
        downloadLayout.AddRow(locationLabel);
        downloadLayout.AddRow(installModel);
        downloadLayout.AddRow(changeLocation);
        downloadLayout.AddRow(cancelDownload);
        downloadLayout.AddRow(installDetails);
        downloads = new Expander { Header = "Reviewed downloads", Expanded = false, Content = downloadLayout };
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
        folderLayout.AddRow(deleteDownload);
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
        layout.AddRow(downloads);
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
        configureCloud.Click += (_, _) =>
        {
            // Hand off with the destination the user picked: "Set up Google…"
            // must land on Google's connection, not on any other provider's.
            if (catalogue.SelectedIndex >= 0 && catalogue.SelectedIndex < ModelCatalogue.Choices.Count)
                openCloudSettings(ModelCatalogue.Choices[catalogue.SelectedIndex].Id);
        };
        configureLocal.Visible = openLocalSettings is not null;
        configureLocal.Click += (_, _) => openLocalSettings?.Invoke();
        catalogue.SelectedIndex = 0;
        ShowCatalogue();
        downloadList.SelectedIndexChanged += (_, _) => ShowPackage();
        installModel.Click += async (_, _) => await InstallSelectedAsync();
        changeLocation.Click += (_, _) => ChangeDownloadLocation();
        cancelDownload.Click += (_, _) => downloadSource?.Cancel();
        deleteDownload.Click += async (_, _) => await DeleteDownloadedFilesAsync();
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
        bool local = item.Id is not ("openai" or "google" or "xai" or "bfl");
        localFolders.Visible = folderDetails.Visible = local;
        // An active download keeps its controls reachable even on another page.
        downloads.Visible = local || downloadSource is not null;
        configureCloud.Visible = !local;
        configureCloud.Text = "Set up " + item.Name.Replace(" image editing", "") + "…";
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
        await RefreshDownloadsAsync();
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
        deleteDownload.Enabled = !busy && Selected is { } folder && managedDestinations.Contains(folder.Directory);
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
        installModel.Enabled = changeLocation.Enabled = deleteDownload.Enabled = false;
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
                deleteDownload.Enabled = Selected is { } folder && managedDestinations.Contains(folder.Directory);
                installModel.Enabled = SelectedPackage is not null;
                changeLocation.Enabled = true;
            }
        }
    }

    // Reviewed downloads: catalogued by the engine, planned before the first
    // byte, reported per completed file, stopped without keeping anything.

    private ReviewedModelPackage? SelectedPackage =>
        downloadList.SelectedIndex >= 0 && downloadList.SelectedIndex < packages.Count ? packages[downloadList.SelectedIndex] : null;

    private async Task RefreshDownloadsAsync()
    {
        try
        {
            packages = await installer.ListPackagesAsync(lifetime);
            var records = await installer.InstallsAsync(lifetime);
            managedDestinations.Clear();
            installedDestinations.Clear();
            foreach (var record in records)
            {
                managedDestinations.Add(record.Destination);
                installedDestinations[record.Id] = record.Destination;
            }
            downloadToolsAvailable = true;
        }
        catch (ProviderException failure)
        {
            packages = [];
            managedDestinations.Clear();
            installedDestinations.Clear();
            downloadToolsAvailable = false;
            packageInfo.Text = failure.Message;
        }
        downloadList.Items.Clear();
        foreach (var package in packages)
            downloadList.Items.Add(new ListItem { Text = package.Display });
        if (packages.Count > 0) downloadList.SelectedIndex = 0;
        ShowPackage();
    }

    private void ShowPackage()
    {
        if (SelectedPackage is not { } package)
        {
            installModel.Enabled = changeLocation.Enabled = false;
            locationLabel.Text = "";
            if (downloadToolsAvailable)
                packageInfo.Text = "No reviewed packages are available for download yet.";
            return;
        }
        packageInfo.Text = package.Display + "\n" + package.Purpose + "\n" +
            "Licence · " + package.Terms + " (the publisher's terms apply; the licence notice travels inside the package)" + "\n" +
            "Download · " + BytesDisplay(package.TotalBytes) +
            (package.FileCount > 1 ? " across " + package.FileCount + " files" : " in one file") +
            (installedDestinations.TryGetValue(package.Id, out var installedAt) ? "\nDownloaded to · " + installedAt : "");
        locationLabel.Text = "Downloads to · " + DownloadDestination(package);
        installModel.Enabled = !busy;
        changeLocation.Enabled = !busy;
    }

    private void ChangeDownloadLocation()
    {
        if (SelectedPackage is not { } package) return;
        using var dialog = new SelectFolderDialog { Title = "Choose the download location" };
        if (dialog.ShowDialog(this) != DialogResult.Ok) return;
        chosenLocations[package.Id] = Path.Combine(dialog.Directory, package.Id);
        locationLabel.Text = "Downloads to · " + chosenLocations[package.Id];
    }

    private async Task InstallSelectedAsync()
    {
        if (busy || lifetime.IsCancellationRequested || SelectedPackage is not { } package) return;
        string destination = DownloadDestination(package);
        ReviewedInstallPlan check;
        try
        {
            check = await installer.PlanAsync(package.Id, destination, lifetime);
        }
        catch (ProviderException failure)
        {
            installDetails.Text = failure.Message;
            return;
        }
        if (!check.Ready)
        {
            installDetails.Text = check.Reason + " Change the download location and try again.";
            return;
        }
        var confirmation = MessageBox.Show(ParentWindow,
            "Download " + BytesDisplay(check.TotalBytes) + " to\n" + destination +
            "\n\nFree space there: " + BytesDisplay(check.FreeBytes) + ". Nothing is sent to a cloud service.",
            "Install model", MessageBoxButtons.YesNo);
        if (confirmation != DialogResult.Yes) return;
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        downloadSource = source;
        SetDownloading(true);
        try
        {
            var result = await installer.InstallAsync(package.Id, destination, installer.ReceiptPath(package.Id),
                progress => Application.Instance.Invoke(() =>
                    installDetails.Text = "Downloading " + progress.Index + " of " + progress.FileCount + " · " + progress.Name),
                source.Token);
            var report = await library.AddAsync(result.Destination, lifetime);
            SetDownloading(false);
            await RefreshAsync(report.Id);
            installDetails.Text = "Installed, awaiting a runner for this computer. The package is listed under Existing model files; " +
                "it cannot render until the reviewed runner for this computer exists.";
        }
        catch (ProviderException failure) { installDetails.Text = failure.Message; }
        catch (OperationCanceledException) { installDetails.Text = "Download stopped. Nothing was kept."; }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
        { installDetails.Text = "Could not run the model download. Check the local setup in Connections."; }
        finally
        {
            downloadSource = null;
            SetDownloading(false);
        }
    }

    private async Task DeleteDownloadedFilesAsync()
    {
        if (busy || Selected is not { } folder || !managedDestinations.Contains(folder.Directory)) return;
        var confirmation = MessageBox.Show(ParentWindow,
            "Delete these downloaded files?\n" + folder.Directory +
            "\n\nOther programs using these files lose access. This also forgets the location in VisoLoom.",
            "Delete downloaded files", MessageBoxButtons.YesNo);
        if (confirmation != DialogResult.Yes) return;
        await RunAsync(async () =>
        {
            await installer.UninstallAsync(folder.Directory, lifetime);
            await library.RemoveAsync(folder.Id, lifetime);
            await RefreshAsync();
            installDetails.Text = "Downloaded files deleted and the location forgotten.";
        });
    }

    private void SetDownloading(bool active)
    {
        busy = active;
        browse.Enabled = refresh.Enabled = folders.Enabled = downloadList.Enabled = plan.Enabled = !active;
        remove.Enabled = !active && Selected is { Configured: false };
        deleteDownload.Enabled = !active && Selected is { } folder && managedDestinations.Contains(folder.Directory);
        catalogue.Enabled = !active;
        installModel.Enabled = !active && SelectedPackage is not null;
        changeLocation.Enabled = !active;
        configureCloud.Enabled = configureLocal.Enabled = !active;
        cancelDownload.Visible = active;
        if (active) installDetails.Text = "Downloading the reviewed package…";
    }

    private string DownloadDestination(ReviewedModelPackage package) =>
        chosenLocations.TryGetValue(package.Id, out var chosen) ? chosen : Path.Combine(DefaultDownloadRoot(), package.Id);

    private static string DefaultDownloadRoot() => Path.Combine(
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
        "VisoLoom", "models");

    private static string BytesDisplay(long bytes) => bytes switch
    {
        >= 1_000_000_000 => (bytes / 1_000_000_000d).ToString("0.##") + " GB",
        >= 1_000_000 => (bytes / 1_000_000d).ToString("0") + " MB",
        _ => bytes.ToString("N0") + " bytes"
    };
}

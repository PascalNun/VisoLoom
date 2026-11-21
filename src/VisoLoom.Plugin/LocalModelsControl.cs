using Eto.Drawing;
using Eto.Forms;
using Rhino;
using System.Text.Json;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal sealed class LocalModelsControl : Panel
{
    private const string DownloadFolderKey = "LocalEngine.DownloadFolder";
    private readonly LocalModelLibrary library = new();
    private readonly LocalModelInstaller installer = new();
    private readonly CancellationToken lifetime;
    private readonly CaptureCoordinator? coordinator;
    private readonly WrappingLabel installed = VisoLoomPanel.CreateLabel("Checking installed models…");
    private readonly Button browseModels = new() { Text = "Browse models…" };
    private readonly Button useExisting = new() { Text = "Use existing files…" };
    private readonly Panel catalogue = new() { Visible = false };
    private readonly Panel catalogueRows = new();
    private readonly Expander moreModels = new() { Header = "More local models", Expanded = false };
    private readonly Panel existingFiles = new() { Visible = false };
    private readonly WrappingLabel locationLabel = VisoLoomPanel.CreateLabel("");
    private readonly WrappingLabel installDetails = VisoLoomPanel.CreateLabel("");
    private readonly Button changeLocation = new() { Text = "Change…" };
    private readonly Button cancelDownload = new() { Text = "Cancel download", Visible = false };
    private readonly Button plan = new() { Text = "Check setup" };
    private readonly WrappingLabel setupDetails = VisoLoomPanel.CreateLabel("");
    private readonly ListBox folders = new() { Height = 82, Width = 1 };
    private readonly WrappingLabel details = VisoLoomPanel.CreateLabel("");
    private readonly Button browse = new() { Text = "Choose folder…" };
    private readonly Button refresh = new() { Text = "Refresh model files" };
    private readonly Button remove = new() { Text = "Forget location" };
    private readonly Button deleteDownload = new() { Text = "Delete downloaded files…" };
    private readonly HashSet<string> managedDestinations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> installedDestinations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> downloadedNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> missingPackages = new(StringComparer.Ordinal);
    private IReadOnlyList<LocalModelFolder> models = [];
    private string downloadRoot;
    private CancellationTokenSource? downloadSource;
    private bool busy;
    private bool loaded;
    private bool inventoryRead;

    public LocalModelsControl(CancellationToken lifetime, Control? localSetup = null)
    {
        this.lifetime = lifetime;
        var settings = VisoLoomPlugIn.Instance?.Settings;
        string? storedFolder = settings?.GetString(DownloadFolderKey, "");
        downloadRoot = Path.IsPathFullyQualified(storedFolder ?? "") ? storedFolder! : DefaultDownloadRoot();
        locationLabel.Text = downloadRoot;
        locationLabel.ToolTip = downloadRoot;
        if (RhinoDoc.ActiveDoc is { } active)
        {
            coordinator = CaptureCoordinator.ForDocument(active.RuntimeSerialNumber);
            coordinator.Changed += CoordinatorChanged;
        }

        var modelDetails = ModelCatalogue.Choices.Where(entry => entry.Location == "This computer" && entry.DownloadPackageId is null)
            .Select(entry => (Control)new Expander
            {
                Header = entry.Name,
                Content = SettingsLayout.Inset(SettingsLayout.Stack(
                    VisoLoomPanel.CreateLabel(entry.Strengths),
                    VisoLoomPanel.CreateLabel(entry.Hardware),
                    VisoLoomPanel.CreateLabel(entry.Storage),
                    VisoLoomPanel.CreateLabel(entry.Setup)))
            });
        moreModels.Content = SettingsLayout.Stack([.. modelDetails]);
        catalogue.Content = SettingsLayout.Inset(SettingsLayout.Stack(
            catalogueRows, moreModels, SettingsLayout.Rule(),
            SettingsLayout.Row(SettingsLayout.Caption("Download folder", locationLabel), changeLocation),
            SettingsLayout.Actions(cancelDownload), installDetails));

        existingFiles.Content = SettingsLayout.Inset(SettingsLayout.Stack(
            SettingsLayout.Actions(browse, refresh), folders, details,
            new Expander { Header = "Setup details", Content = SettingsLayout.Inset(setupDetails) },
            SettingsLayout.Actions(plan, remove), SettingsLayout.Actions(deleteDownload)));

        var actions = new List<Control> { installed, SettingsLayout.Actions(browseModels, useExisting), catalogue, existingFiles };
        if (localSetup is not null)
            actions.Add(new Expander { Header = "Local engine", Expanded = false, Content = SettingsLayout.Inset(localSetup) });
        Content = SettingsLayout.Stack([.. actions]);

        browseModels.Click += (_, _) =>
        {
            catalogue.Visible = !catalogue.Visible;
            if (catalogue.Visible) existingFiles.Visible = false;
        };
        useExisting.Click += (_, _) =>
        {
            existingFiles.Visible = !existingFiles.Visible;
            if (existingFiles.Visible)
            {
                catalogue.Visible = false;
                browse.Focus();
            }
        };
        changeLocation.Click += (_, _) =>
        {
            using var dialog = new SelectFolderDialog { Title = "Choose the download folder" };
            if (dialog.ShowDialog(this) != DialogResult.Ok || !Path.IsPathFullyQualified(dialog.Directory)) return;
            downloadRoot = dialog.Directory;
            settings?.SetString(DownloadFolderKey, downloadRoot);
            locationLabel.Text = downloadRoot;
            locationLabel.ToolTip = downloadRoot;
        };
        cancelDownload.Click += (_, _) => downloadSource?.Cancel();
        deleteDownload.Click += async (_, _) => await DeleteDownloadedFilesAsync();
        folders.SelectedIndexChanged += (_, _) => ShowSelection();
        refresh.Click += async (_, _) => await RunAsync(() => RefreshAsync());
        browse.Click += async (_, _) =>
        {
            using var dialog = new SelectFolderDialog { Title = "Choose the model’s folder" };
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
                details.Text = report.State == "connectionCheckNeeded" ? "Files verified. Check the local engine before rendering." : report.Message;
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
            await RunAsync(() => RefreshAsync());
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

    private void CoordinatorChanged(object? sender, EventArgs e)
    {
        if (!lifetime.IsCancellationRequested)
            RefreshInstalled();
    }

    private void RefreshInstalled()
    {
        var names = new List<string>();
        if (coordinator?.LocalModel is { } running)
            names.Add(running.DisplayName + " · Connected");
        foreach (var (packageId, name) in downloadedNames)
        {
            var profile = ModelCatalogue.Choices.FirstOrDefault(entry => entry.DownloadPackageId == packageId)?.EngineProfileId;
            if (profile is null || profile != coordinator?.LocalModel?.Id)
                names.Add(name + " · Downloaded");
        }
        int linked = models.Count(model => !managedDestinations.Contains(model.Directory));
        if (linked != 0)
            names.Add(linked == 1 ? "1 existing folder linked" : $"{linked} existing folders linked");
        installed.Text = names.Count != 0 ? string.Join("\n", names)
            : inventoryRead ? "No models installed" : "Checking installed models…";
    }

    private async Task RefreshAsync(string? selected = null)
    {
        await RefreshDownloadsAsync();
        var updated = await library.ListAsync(lifetime);
        lifetime.ThrowIfCancellationRequested();
        inventoryRead = true;
        models = updated;
        RefreshInstalled();
        folders.Items.Clear();
        foreach (var model in models)
            folders.Items.Add(new ListItem { Text = model.Name });
        folders.Visible = models.Count != 0;
        folders.SelectedIndex = selected is null ? (models.Count == 0 ? -1 : 0) : models.ToList().FindIndex(model => model.Id == selected);
        if (models.Count == 0) details.Text = "No model folders saved.";
        ShowSelection();
    }

    private void ShowSelection()
    {
        plan.Visible = Selected is not null;
        remove.Visible = Selected is { Configured: false };
        deleteDownload.Visible = Selected is { } folder && managedDestinations.Contains(folder.Directory);
        if (Selected is not { } model) { setupDetails.Text = ""; return; }
        details.Text = model.FileCount + " files · " + BytesDisplay(model.Bytes) + "\n" +
            (model.Configured && model.Readiness == "ready" ? "Files verified. Check the local engine before rendering." : "Setup needed.");
        setupDetails.Text = model.Directory + "\n\n" + (model.ReadinessMessage ?? model.Message);
    }

    private async Task RunAsync(Func<Task> work)
    {
        if (busy || lifetime.IsCancellationRequested) return;
        SetBusy(true);
        details.Text = "Checking model folders…";
        try { await work(); }
        catch (ProviderException ex) { if (!lifetime.IsCancellationRequested) ReportFolderError(ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { if (!lifetime.IsCancellationRequested) ReportFolderError("Could not read the model files. Check the folder or local engine setup."); }
        catch (OperationCanceledException)
        { if (!lifetime.IsCancellationRequested) ReportFolderError("The folder check timed out. Choose a folder on an available drive."); }
        finally { if (!lifetime.IsCancellationRequested) SetBusy(false); }
    }

    private void ReportFolderError(string message)
    {
        details.Text = message;
        if (!inventoryRead)
            installed.Text = "Could not check installed models.";
    }

    private async Task RefreshDownloadsAsync()
    {
        IReadOnlyList<ReviewedModelPackage> packages;
        try
        {
            packages = await installer.ListPackagesAsync(lifetime);
            var records = await installer.InstallsAsync(lifetime);
            lifetime.ThrowIfCancellationRequested();
            managedDestinations.Clear();
            installedDestinations.Clear();
            missingPackages.Clear();
            downloadedNames.Clear();
            foreach (var record in records)
            {
                if (!Directory.Exists(record.Destination))
                {
                    missingPackages.Add(record.Id);
                    continue;
                }
                managedDestinations.Add(record.Destination);
                installedDestinations[record.Id] = record.Destination;
                downloadedNames[record.Id] = ModelCatalogue.Choices.FirstOrDefault(entry => entry.DownloadPackageId == record.Id)?.Name
                    ?? packages.FirstOrDefault(package => package.Id == record.Id)?.Display
                    ?? "Local model";
            }
        }
        catch (ProviderException failure)
        {
            if (!lifetime.IsCancellationRequested)
                ReplaceCatalogue(VisoLoomPanel.CreateLabel(failure.Message));
            return;
        }
        if (packages.Count == 0)
        {
            ReplaceCatalogue(VisoLoomPanel.CreateLabel("No models available for download."));
            return;
        }
        var rows = new List<Control>();
        foreach (var package in packages)
        {
            if (rows.Count != 0) rows.Add(SettingsLayout.Rule());
            var entry = ModelCatalogue.Choices.FirstOrDefault(item => item.DownloadPackageId == package.Id);
            string? destination = installedDestinations.GetValueOrDefault(package.Id);
            string availability = destination is not null ? "Downloaded" : missingPackages.Contains(package.Id) ? "Files missing" : BytesDisplay(package.TotalBytes);
            var download = new Button { Text = destination is null ? "Download" : "Installed", Enabled = destination is null };
            download.Click += async (_, _) => await InstallAsync(package);
            var information = SettingsLayout.Stack(
                VisoLoomPanel.CreateLabel(package.Purpose),
                VisoLoomPanel.CreateLabel("Download · " + BytesDisplay(package.TotalBytes)),
                VisoLoomPanel.CreateLabel("Licence · " + package.Terms));
            if (entry is not null)
                information.Items.Insert(1, VisoLoomPanel.CreateLabel(entry.Hardware));
            if (destination is not null)
                information.Items.Add(VisoLoomPanel.CreateLabel(destination));
            rows.Add(SettingsLayout.Stack(
                SettingsLayout.Row(SettingsLayout.Caption(entry?.Name ?? package.Display, VisoLoomPanel.CreateLabel(availability)), download),
                new Expander { Header = "Model details", Content = SettingsLayout.Inset(information) }));
        }
        ReplaceCatalogue(SettingsLayout.Stack([.. rows]));
    }

    private void ReplaceCatalogue(Control content)
    {
        var previous = catalogueRows.Content;
        catalogueRows.Content = content;
        previous?.Dispose();
    }

    private async Task InstallAsync(ReviewedModelPackage package)
    {
        if (busy || lifetime.IsCancellationRequested) return;
        string destination = Path.Combine(downloadRoot, package.Id);
        SetBusy(true);
        try
        {
            var check = await installer.PlanAsync(package.Id, destination, lifetime);
            lifetime.ThrowIfCancellationRequested();
            if (!check.Ready)
            {
                installDetails.Text = check.Reason;
                return;
            }
            if (MessageBox.Show(ParentWindow, "Download " + BytesDisplay(check.TotalBytes) + " to\n" + destination +
                "\n\nAvailable space: " + BytesDisplay(check.FreeBytes),
                "Download model", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            downloadSource = source;
            cancelDownload.Visible = true;
            installDetails.Text = "Downloading model files…";
            var result = await installer.InstallAsync(package.Id, destination, installer.ReceiptPath(package.Id),
                progress => Application.Instance.Invoke(() =>
                {
                    if (!lifetime.IsCancellationRequested)
                        installDetails.Text = "Downloading " + progress.Index + " of " + progress.FileCount + " · " + progress.Name;
                }), source.Token);
            var report = await library.AddAsync(result.Destination, lifetime);
            await RefreshAsync(report.Id);
            installDetails.Text = "Downloaded and verified. Check the local engine to make the model available in Render.";
        }
        catch (ProviderException ex) { if (!lifetime.IsCancellationRequested) installDetails.Text = ex.Message; }
        catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) installDetails.Text = "Download stopped."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
        { if (!lifetime.IsCancellationRequested) installDetails.Text = "Could not complete the download. Check the folder and try again."; }
        finally
        {
            downloadSource = null;
            if (!lifetime.IsCancellationRequested)
            {
                cancelDownload.Visible = false;
                SetBusy(false);
            }
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
            details.Text = "Downloaded files deleted.";
        });
    }

    private void SetBusy(bool active)
    {
        busy = active;
        browse.Enabled = refresh.Enabled = folders.Enabled = plan.Enabled = remove.Enabled = deleteDownload.Enabled = !active;
        catalogueRows.Enabled = changeLocation.Enabled = !active;
        browseModels.Enabled = useExisting.Enabled = !active;
    }

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

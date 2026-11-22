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
    private readonly Panel installed = new() { Visible = false };
    private readonly WrappingLabel inventoryNote = SettingsLayout.Secondary("Checking installed models…");
    private (string Name, string Status, bool Connected)[] installedRows = [];
    private readonly WrappingLabel engineStatus = SettingsLayout.Secondary("Local engine · Off");
    private readonly Button checkEngine = new() { Text = "Check engine" };
    private readonly Dictionary<string, WrappingLabel> installStatus = new(StringComparer.Ordinal);
    private bool engineBusy;
    private readonly HashSet<string> openGates = new(StringComparer.Ordinal);
    private static readonly string[] PrimaryCatalogueIds =
        ["local-klein-native", "flux3-image-klein", "flux3-image-klein-base", "qwen-image-edit-2511", "longcat-image-edit"];
    private static readonly string[] MoreCatalogueIds =
        ["flux2-klein-9b", "flux2-dev", "llada-image-turbo"];
    private readonly Button browseModels = new() { Text = "Browse models…" };
    private readonly Button useExisting = new() { Text = "Use existing files…" };
    private readonly Panel catalogue = new() { Visible = false };
    // The Hugging Face token row (ADR 0032): entered once, stored in the OS
    // credential store, needed only by gated model files.
    private readonly PasswordBox tokenEntry = new() { Width = 1 };
    private readonly Button saveToken = new() { Text = "Save token", Enabled = false };
    private readonly Button forgetToken = new() { Text = "Forget", Visible = false };
    private readonly WrappingLabel tokenStatus = SettingsLayout.Secondary("No token saved");
    private bool tokenReady;
    private readonly Panel catalogueRows = new();
    private readonly Expander moreModels = new() { Header = "More local models", Expanded = false };
    private readonly Panel existingFiles = new() { Visible = false };
    private readonly WrappingLabel locationLabel = SettingsLayout.Secondary("");
    private readonly WrappingLabel installDetails = VisoLoomPanel.CreateLabel("");
    private readonly Button changeLocation = new() { Text = "Change…" };
    private readonly Button cancelDownload = new() { Text = "Cancel download" };
    private readonly StackLayout downloadActions;
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

    public LocalModelsControl(CancellationToken lifetime)
    {
        this.lifetime = lifetime;
        var settings = VisoLoomPlugIn.Instance?.Settings;
        string? storedFolder = settings?.GetString(DownloadFolderKey, "");
        downloadRoot = Path.IsPathFullyQualified(storedFolder ?? "") ? storedFolder! : DefaultDownloadRoot();
        ShowDownloadFolder();
        if (RhinoDoc.ActiveDoc is { } active)
        {
            coordinator = CaptureCoordinator.ForDocument(active.RuntimeSerialNumber);
            coordinator.Changed += CoordinatorChanged;
        }
        ConnectionSettings.Changed += CoordinatorChanged;

        moreModels.Content = SettingsLayout.Stack([.. MoreCatalogueIds
            .Select(id => ModelCatalogue.Choices.FirstOrDefault(entry => entry.Id == id))
            .OfType<ModelCatalogueEntry>()
            .Select(GatedRow)]);
        downloadActions = SettingsLayout.Actions(cancelDownload);
        downloadActions.Visible = false;
        installDetails.Visible = false;
        // The token row sits beside the download folder: a credential for
        // gated files, disclosed the same way as the provider keys.
        tokenEntry.ID = "huggingface-token";
        var tokenHint = new LinkButton { Text = "Create a token on huggingface.co", ToolTip = "https://huggingface.co/settings/tokens" };
        tokenHint.Click += (_, _) => OpenWeb("https://huggingface.co/settings/tokens");
        tokenEntry.TextChanged += (_, _) => saveToken.Enabled = tokenEntry.Text.Length != 0;
        saveToken.Click += async (_, _) =>
        {
            try
            {
                ConnectionSettings.SetHuggingFaceToken(tokenEntry.Text);
                tokenEntry.Text = "";
            }
            catch (InvalidOperationException ex) { tokenStatus.Text = ex.Message; }
        };
        forgetToken.Click += (_, _) => ConnectionSettings.SetHuggingFaceToken(null);
        catalogue.Content = SettingsLayout.Inset(SettingsLayout.Stack(
            catalogueRows, moreModels, SettingsLayout.Rule(),
            SettingsLayout.Row(SettingsLayout.Caption("Download folder", locationLabel), changeLocation),
            // The entry sits on its own full-width line, the provider key
            // form's shape: a side-by-side row would collapse it to Width = 1.
            SettingsLayout.Caption("Hugging Face token", tokenStatus),
            tokenEntry,
            SettingsLayout.Actions(saveToken, forgetToken, tokenHint),
            SettingsLayout.Secondary("Needed only for gated model files. Sent only to huggingface.co as the download's authorization header; never stored in files or logs."),
            downloadActions, installDetails));
        RefreshTokenRow();

        existingFiles.Content = SettingsLayout.Inset(SettingsLayout.Stack(
            SettingsLayout.Actions(browse, refresh), folders, details,
            new Expander { Header = "Setup details", Content = SettingsLayout.Inset(setupDetails) },
            SettingsLayout.Actions(plan, remove), SettingsLayout.Actions(deleteDownload)));

        var actions = new List<Control>
        {
            installed, inventoryNote,
            SettingsLayout.Row(SettingsLayout.Caption("Local engine", engineStatus), checkEngine),
            SettingsLayout.Actions(browseModels, useExisting), catalogue, existingFiles
        };
        Content = SettingsLayout.Stack([.. actions]);
        checkEngine.Click += async (_, _) => await CheckEngineAsync();

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
            ShowDownloadFolder();
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
        if (disposing)
        {
            ConnectionSettings.Changed -= CoordinatorChanged;
            if (coordinator is { } session)
                session.Changed -= CoordinatorChanged;
        }
        base.Dispose(disposing);
    }

    private void CoordinatorChanged(object? sender, EventArgs e)
    {
        if (lifetime.IsCancellationRequested)
            return;
        bool hadToken = tokenReady;
        RefreshInstalled();
        RefreshTokenRow();
        // The catalogue is rebuilt only when the token's presence changed:
        // its Download buttons unlock for gated packages exactly at that
        // moment, and rows built moments ago stay as they were otherwise.
        if (tokenReady != hadToken)
            _ = RunAsync(() => RefreshAsync());
    }

    private void RefreshTokenRow()
    {
        tokenReady = ConnectionSettings.HuggingFaceIsReady;
        tokenStatus.Text = tokenReady ? "Token saved" : "No token saved";
        SettingsLayout.ConnectionStatus(tokenStatus, tokenReady);
        forgetToken.Visible = tokenReady;
        if (tokenEntry.Text.Length != 0)
            tokenEntry.Text = "";
    }

    private void RefreshInstalled()
    {
        var rows = new List<(string Name, string Status, bool Connected)>();
        string? runningProfile = null;
        if (coordinator?.LocalModel is { } running)
        {
            var entry = ModelCatalogue.Choices.FirstOrDefault(choice => choice.EngineProfileId == running.Id);
            rows.Add((entry?.Name ?? running.DisplayName, "Connected", true));
            runningProfile = running.Id;
        }
        var managed = managedDestinations.ToHashSet(StringComparer.Ordinal);
        foreach (var model in models)
        {
            var profile = ModelCatalogue.Choices.FirstOrDefault(choice => choice.DownloadPackageId is { } packageId && installedDestinations.GetValueOrDefault(packageId) == model.Directory)?.EngineProfileId;
            if (profile is not null && profile == runningProfile) continue;
            rows.Add((model.Name, managed.Contains(model.Directory) ? "Installed" : "Existing files linked", false));
        }
        foreach (var (packageId, name) in downloadedNames)
        {
            string? destination = installedDestinations.GetValueOrDefault(packageId);
            if (destination is not null && models.Any(model => model.Directory == destination)) continue;
            var profile = ModelCatalogue.Choices.FirstOrDefault(entry => entry.DownloadPackageId == packageId)?.EngineProfileId;
            if (profile is not null && profile == runningProfile) continue;
            rows.Add((name, "Installed", false));
        }
        if (!installedRows.SequenceEqual(rows))
        {
            installedRows = [.. rows];
            var captions = installedRows.Select(row =>
            {
                var status = SettingsLayout.Secondary(row.Status);
                SettingsLayout.ConnectionStatus(status, row.Connected);
                return (Control)SettingsLayout.Caption(row.Name, status);
            });
            var previous = installed.Content;
            installed.Content = SettingsLayout.Stack([.. captions]);
            installed.Visible = installedRows.Length != 0;
            previous?.Dispose();
        }
        engineStatus.Text = coordinator?.LocalModel is { } runningEngine
            ? "Local engine · Running · " + runningEngine.DisplayName
            : "Local engine · Off";
        inventoryNote.Text = installedRows.Length != 0
            ? ""
            : inventoryRead ? "No models installed" : "Checking installed models…";
        inventoryNote.Visible = inventoryNote.Text.Length != 0;
    }

    private async Task CheckEngineAsync()
    {
        if (engineBusy || lifetime.IsCancellationRequested) return;
        if (coordinator is null) { engineStatus.Text = "Open a Rhino model first."; return; }
        engineBusy = true;
        checkEngine.Enabled = false;
        engineStatus.Text = "Local engine · Checking…";
        try
        {
            var local = await coordinator.CheckLocalEngineAsync();
            if (lifetime.IsCancellationRequested) return;
            engineStatus.Text = "Local engine · Running · " + local.DisplayName;
        }
        catch (Exception ex) when (ex is ProviderException or InvalidOperationException)
        { if (!lifetime.IsCancellationRequested) engineStatus.Text = ex.Message; }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        { if (!lifetime.IsCancellationRequested) engineStatus.Text = "The local engine did not respond. Start it and check again."; }
        finally
        {
            engineBusy = false;
            if (!lifetime.IsCancellationRequested)
                checkEngine.Enabled = true;
        }
    }

    private void ShowDownloadFolder()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        locationLabel.Text = !OperatingSystem.IsWindows() && home.Length != 0 && downloadRoot.StartsWith(home + "/", StringComparison.Ordinal)
            ? "~" + downloadRoot[home.Length..]
            : downloadRoot;
        locationLabel.ToolTip = downloadRoot;
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
        {
            inventoryNote.Text = "Could not check installed models.";
            inventoryNote.Visible = true;
        }
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
            if (lifetime.IsCancellationRequested) return;
            var notice = new List<Control> { VisoLoomPanel.CreateLabel(failure.Message) };
            notice.AddRange(PrimaryCatalogueIds.Select(CatalogueRow).OfType<Control>());
            ReplaceCatalogue(SettingsLayout.Stack([.. notice]));
            return;
        }
        if (packages.Count == 0)
        {
            var notice = new List<Control> { VisoLoomPanel.CreateLabel("No models are available for download right now.") };
            notice.AddRange(PrimaryCatalogueIds.Select(CatalogueRow).OfType<Control>());
            ReplaceCatalogue(SettingsLayout.Stack([.. notice]));
            return;
        }
        installStatus.Clear();
        var rows = new List<Control>();
        var packagesById = packages.ToDictionary(package => package.Id);
        foreach (var id in PrimaryCatalogueIds)
        {
            var entry = ModelCatalogue.Choices.FirstOrDefault(item => item.Id == id);
            if (entry is null) continue;
            if (entry.DownloadPackageId is { } packageId && packagesById.TryGetValue(packageId, out var package))
                rows.Add(PackageRow(package, entry));
            else if (entry.PublisherUrl is not null)
            {
                if (rows.Count != 0) rows.Add(SettingsLayout.Rule());
                rows.Add(GatedRow(entry));
            }
        }
        ReplaceCatalogue(SettingsLayout.Stack([.. rows]));
    }

    private Control PackageRow(ReviewedModelPackage package, ModelCatalogueEntry? entry)
    {
        string? destination = installedDestinations.GetValueOrDefault(package.Id);
        string size = BytesDisplay(package.TotalBytes);
        string availability = missingPackages.Contains(package.Id) && destination is null ? "Files missing · " + size : size;
        // A gated package needs the user's publisher token; acceptance of the
        // licence itself stays on the publisher's page (ADR 0032).
        bool gated = package.RequiresToken && !ConnectionSettings.HuggingFaceIsReady;
        var download = new Button
        {
            Text = destination is null ? gated ? "Needs token" : "Download" : "Installed",
            Enabled = destination is null && !gated
        };
        download.Click += async (_, _) => await InstallAsync(package);
        var progress = VisoLoomPanel.CreateLabel("");
        progress.Visible = false;
        installStatus[package.Id] = progress;
        var detail = new List<string> { package.Purpose };
        if (entry is not null)
        {
            detail.Add(entry.Hardware);
            detail.Add(entry.Storage);
        }
        var name = VisoLoomPanel.CreateLabel(entry?.Name ?? package.Display, bold: true);
        name.ToolTip = string.Join("\n", detail);
        var rowText = new List<Control> { name, SettingsLayout.Secondary(entry?.Fit ?? availability) };
        if (destination is not null)
            rowText.Add(SettingsLayout.Secondary(destination));
        else if (package.RequiresToken && ConnectionSettings.HuggingFaceIsReady)
            rowText.Add(SettingsLayout.Secondary("Gated files · uses the saved token; the licence stays between you and the publisher."));
        else if (gated)
            rowText.Add(SettingsLayout.Secondary("Gated files · save a Hugging Face token below to download."));
        else if (package.Terms.Length != 0)
            rowText.Add(SettingsLayout.Secondary("Licence · " + package.Terms));
        // Licensed downloads keep the publisher and licence links on the row;
        // terms stay the publisher's to review even after the download exists.
        var licenceLinks = new List<Control>();
        if (entry?.PublisherUrl is { } publisher)
        {
            var publisherLink = new LinkButton { Text = "Publisher weights", ToolTip = publisher };
            publisherLink.Click += (_, _) => OpenWeb(publisher);
            licenceLinks.Add(publisherLink);
        }
        if (entry?.LicenceUrl is { } licence)
        {
            var licenceLink = new LinkButton { Text = "Licence terms", ToolTip = licence };
            licenceLink.Click += (_, _) => OpenWeb(licence);
            licenceLinks.Add(licenceLink);
        }
        var row = SettingsLayout.Stack(
            SettingsLayout.Row(SettingsLayout.Stack([.. rowText]), download),
            progress);
        if (licenceLinks.Count != 0)
            row.Items.Add(SettingsLayout.Actions([.. licenceLinks]));
        return row;
    }

    // A primary card without a reviewed package falls back to its publisher
    // card — the package list is unavailable, the reference stays honest.
    private Control? CatalogueRow(string id)
    {
        var entry = ModelCatalogue.Choices.FirstOrDefault(item => item.Id == id);
        return entry is null || entry.DownloadPackageId is not null ? null : GatedRow(entry);
    }

    private Control GatedRow(ModelCatalogueEntry entry)
    {
        var weights = new Button { Text = "Get weights…" };
        var panel = new Panel { Visible = openGates.Contains(entry.Id) };
        var gateLines = new List<Control>();
        if (entry.WeightsNote is { } note)
            gateLines.Add(SettingsLayout.Secondary(note));
        if (entry.PublisherUrl is { } publisher)
        {
            var publisherLink = new LinkButton { Text = "Publisher weights", ToolTip = publisher };
            publisherLink.Click += (_, _) => OpenWeb(publisher);
            gateLines.Add(publisherLink);
        }
        if (entry.LicenceUrl is { } licence)
        {
            var licenceLink = new LinkButton { Text = "Licence terms", ToolTip = licence };
            licenceLink.Click += (_, _) => OpenWeb(licence);
            gateLines.Add(licenceLink);
        }
        var useExisting = new Button { Text = "Use existing files…" };
        useExisting.Click += (_, _) =>
        {
            catalogue.Visible = false;
            existingFiles.Visible = true;
            browse.Focus();
        };
        gateLines.Add(SettingsLayout.Actions(useExisting));
        panel.Content = SettingsLayout.Inset(SettingsLayout.Stack([.. gateLines]));
        weights.Click += (_, _) =>
        {
            panel.Visible = !panel.Visible;
            if (panel.Visible) openGates.Add(entry.Id); else openGates.Remove(entry.Id);
        };
        var name = VisoLoomPanel.CreateLabel(entry.Name, bold: true);
        name.ToolTip = entry.Strengths + "\n" + entry.Hardware + "\n" + entry.Storage + "\n" + entry.Setup;
        var rowText = new List<Control> { name };
        if (entry.Fit is { } fit)
            rowText.Add(SettingsLayout.Secondary(fit));
        return SettingsLayout.Stack(
            SettingsLayout.Row(SettingsLayout.Stack([.. rowText]), weights),
            panel);
    }

    private static void OpenWeb(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
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
                ShowInstallStatus(check.Reason, package.Id);
                return;
            }
            if (MessageBox.Show(ParentWindow, "Download " + BytesDisplay(check.TotalBytes) + " to\n" + destination +
                "\n\nAvailable space: " + BytesDisplay(check.FreeBytes),
                "Download model", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            downloadSource = source;
            downloadActions.Visible = true;
            ShowInstallStatus("Downloading model files…", package.Id);
            var result = await installer.InstallAsync(package.Id, destination, installer.ReceiptPath(package.Id),
                progress => Application.Instance.Invoke(() =>
                {
                    if (!lifetime.IsCancellationRequested)
                        ShowInstallStatus("Downloading " + progress.Index + " of " + progress.FileCount + " · " + progress.Name, package.Id);
                }), source.Token, ConnectionSettings.HuggingFaceToken);
            var report = await library.AddAsync(result.Destination, lifetime);
            await RefreshAsync(report.Id);
            ShowInstallStatus("Downloaded and verified. Check the local engine to make the model available in Render.", package.Id);
        }
        catch (ProviderException ex) { if (!lifetime.IsCancellationRequested) ShowInstallStatus(ex.Message, package.Id); }
        catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) ShowInstallStatus("Download stopped.", package.Id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
        { if (!lifetime.IsCancellationRequested) ShowInstallStatus("Could not complete the download. Check the folder and try again.", package.Id); }
        finally
        {
            downloadSource = null;
            if (!lifetime.IsCancellationRequested)
            {
                downloadActions.Visible = false;
                SetBusy(false);
            }
        }
    }

    private void ShowInstallStatus(string message, string? packageId = null)
    {
        var label = packageId is null ? installDetails : installStatus.GetValueOrDefault(packageId) ?? installDetails;
        label.Text = message;
        label.Visible = message.Length != 0;
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

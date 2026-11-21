using Eto.Drawing;
using Eto.Forms;
using Rhino;
using VisoLoom.Core;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

// The Settings Connections view ([information architecture](docs/ux/information-architecture.md),
// ADR 0028). Every destination VisoLoom can render with is one saved connection
// in the list, shown with its current state and the reason if it cannot render.
// Selecting an entry shows that connection's provider, destination, status,
// supported models and its explicit Check connection / Disconnect actions.
// Add connection… is the one entry point for connecting another destination.
// Nearly identical provider forms are replaced by one data-described flow, so
// a fifth cloud provider is added by extending CloudConnection, not by adding
// another form. Multiple saved connections per provider and office servers stay
// future work; the first implementation is one saved connection per provider.
internal sealed class ConnectionsView : Panel
{
    // The per-provider UI contract, written once as data. Configure takes
    // (key, remember, includeNames); providers without scene names ignore the
    // last argument instead of growing another signature.
    private sealed record CloudConnection(
        string Id,
        string Name,
        string Destination,
        string ConnectedNote,
        string KeyHint,
        string NotCheckedNote,
        string[] ConsentRows,
        string[]? BillingRows,
        bool OffersIncludeNames,
        bool RenderingOff,
        string TestedModelId,
        string TestedLabel,
        string ListedLabel,
        Func<bool> Allowed,
        Func<bool> Ready,
        Func<bool> RememberSaved,
        Func<string> CurrentKey,
        Func<IReadOnlyList<ModelCapabilities>> Models,
        Action<IReadOnlyList<ModelCapabilities>> PublishModels,
        Func<string, CancellationToken, Task<ProviderCapabilities>> Discover,
        Func<string, bool, bool, string> Configure,
        Action Disable,
        Action Disconnect,
        Func<int, string> ResultNote);

    private sealed class CloudWidgets
    {
        public required PasswordBox Secret { get; init; }
        public required CheckBox Allow { get; init; }
        public required CheckBox Remember { get; init; }
        public CheckBox? Names { get; init; }
        public required Button Connect { get; init; }
        public required Button Forget { get; init; }
        public required WrappingLabel Status { get; init; }
        public required WrappingLabel Supported { get; init; }
    }

    private readonly CancellationToken lifetime;
    private readonly ListBox list = new() { Height = 148, Width = 1 };
    private readonly Panel details = new();
    private readonly Button addConnection = new() { Text = "Add connection…" };
    private readonly IReadOnlyList<CloudConnection> providers;
    private readonly Dictionary<string, CloudConnection> cloudsById = [];
    private readonly Dictionary<string, CloudWidgets> widgetsById = [];
    private readonly Dictionary<string, Control> panelsById = [];
    private readonly string[] entryIds;
    private readonly CaptureCoordinator? coordinator;
    private readonly WrappingLabel localStatus = VisoLoomPanel.CreateLabel("");
    private readonly Button checkLocal = new() { Text = "Check local connection" };
    // The native settings check targets this box to verify the Models-tab
    // handoff lands in the Connections view with the key field focused.
    private readonly PasswordBox openAiSecret;

    internal ConnectionsView(CancellationToken lifetimeToken, Action<Control>? configureLocal = null)
    {
        lifetime = lifetimeToken;
        providers = [OpenAi(), Google(), Xai(), Bfl()];
        entryIds = ["local", .. providers.Select(provider => provider.Id)];
        if (RhinoDoc.ActiveDoc is { } active)
            coordinator = CaptureCoordinator.ForDocument(active.RuntimeSerialNumber);
        foreach (var cloud in providers)
        {
            cloudsById[cloud.Id] = cloud;
            (widgetsById[cloud.Id], panelsById[cloud.Id]) = BuildCloud(cloud);
        }
        panelsById["local"] = BuildLocal(configureLocal);
        openAiSecret = widgetsById["openai"].Secret;
        foreach (string _ in entryIds)
            list.Items.Add(new ListItem());
        RefreshLocal();
        RefreshSummaries();
        var layout = new DynamicLayout { Spacing = new Size(8, 8) };
        layout.AddRow(VisoLoomPanel.CreateLabel("Connections", bold: true));
        layout.AddRow(VisoLoomPanel.CreateLabel("Each destination is one saved connection. Select one to check, configure or disconnect it. Checking tests the key and lists the account's models; no image is sent."));
        layout.AddRow(list);
        layout.AddRow(details);
        layout.AddRow(addConnection);
        Content = layout;
        addConnection.Click += (_, _) => AddConnection();
        list.SelectedIndexChanged += (_, _) => ShowSelected();
        list.SelectedIndex = 0;
        ShowSelected();
        ConnectionSettings.Changed += ConnectionChanged;
        if (coordinator is { } session)
            session.Changed += CoordinatorChanged;
    }

    private static CloudConnection OpenAi() => new(
        Id: "openai",
        Name: "OpenAI",
        Destination: "Cloud · api.openai.com",
        ConnectedNote: "Connected. Choose an image model in the Render panel.",
        KeyHint: "Leave blank to keep the current key. API billing is separate from your ChatGPT subscription.",
        NotCheckedNote: "Not checked this session. The Model menu offers " + OpenAiImageProvider.DisplayName + " · tested.",
        ConsentRows: ["Saved keys use this computer's secure credential storage.", "Render sends one source image, your prompt and scene guidance. Connection checks send no image."],
        BillingRows: ["Render sends one source image, the representation and prompt, camera and frame, units, clipping planes and material summaries. Layer/material names and material descriptions are included only when enabled.", "Connection checks send no image. Rendering requests one image at medium quality. Stop waiting ends local waiting while OpenAI may still finish and charge the request."],
        OffersIncludeNames: true,
        RenderingOff: false,
        TestedModelId: OpenAiImageProvider.ModelId,
        TestedLabel: "tested",
        ListedLabel: "cloud",
        Allowed: () => ConnectionSettings.CloudAllowed,
        Ready: () => ConnectionSettings.IsReady,
        RememberSaved: () => ConnectionSettings.RememberKey,
        CurrentKey: () => ConnectionSettings.ApiKey,
        Models: () => ConnectionSettings.AvailableOpenAiModels,
        PublishModels: models => ConnectionSettings.SetAvailableOpenAiModels(models),
        Discover: async (key, token) =>
        {
            using var provider = new OpenAiImageProvider(key, true, new RhinoImageProcessor());
            return await provider.DiscoverAsync(token);
        },
        Configure: (key, remember, includeNames) => ConnectionSettings.Configure(key, includeNames, remember),
        Disable: () => ConnectionSettings.DisableCloud(),
        Disconnect: () => ConnectionSettings.Disconnect(),
        ResultNote: ImageModelsNote);

    private static CloudConnection Google() => new(
        Id: "google",
        Name: "Google",
        Destination: "Cloud · generativelanguage.googleapis.com",
        ConnectedNote: "Connected. Choose an image model in the Render panel.",
        KeyHint: "Leave blank to keep the current key. Google bills this API key's own account.",
        NotCheckedNote: "Not checked this session. The Model menu offers " + GoogleImageProvider.AccountModel(GoogleImageProvider.ModelId).DisplayName + " · tested.",
        ConsentRows: ["Saved keys use this computer's secure credential storage. Render sends one source image, your prompt and scene guidance."],
        BillingRows: null,
        OffersIncludeNames: false,
        RenderingOff: false,
        TestedModelId: GoogleImageProvider.ModelId,
        TestedLabel: "tested",
        ListedLabel: "cloud",
        Allowed: () => ConnectionSettings.GoogleAllowed,
        Ready: () => ConnectionSettings.GoogleIsReady,
        RememberSaved: () => ConnectionSettings.GoogleRememberKey,
        CurrentKey: () => ConnectionSettings.GoogleApiKey,
        Models: () => ConnectionSettings.AvailableGoogleModels,
        PublishModels: models => ConnectionSettings.SetAvailableGoogleModels(models),
        Discover: async (key, token) =>
        {
            using var provider = new GoogleImageProvider(key, true, new RhinoImageProcessor());
            return await provider.DiscoverAsync(token);
        },
        Configure: (key, remember, _) => ConnectionSettings.ConfigureGoogle(key, remember),
        Disable: () => ConnectionSettings.DisableGoogle(),
        Disconnect: () => ConnectionSettings.DisconnectGoogle(),
        ResultNote: ImageModelsNote);

    private static CloudConnection Xai() => new(
        Id: "xai",
        Name: "xAI",
        Destination: "Cloud · api.x.ai",
        ConnectedNote: "Connected. Choose an image model in the Render panel.",
        KeyHint: "Leave blank to keep the current key. xAI bills this API key's own account.",
        NotCheckedNote: "Not checked this session. The Model menu offers " + XaiImageProvider.AccountModel(XaiImageProvider.ModelId).DisplayName + " · tested.",
        ConsentRows: ["Saved keys use this computer's secure credential storage. Render sends one source image, your prompt and scene guidance."],
        BillingRows: null,
        OffersIncludeNames: false,
        RenderingOff: false,
        TestedModelId: XaiImageProvider.ModelId,
        TestedLabel: "tested",
        ListedLabel: "cloud",
        Allowed: () => ConnectionSettings.XaiAllowed,
        Ready: () => ConnectionSettings.XaiIsReady,
        RememberSaved: () => ConnectionSettings.XaiRememberKey,
        CurrentKey: () => ConnectionSettings.XaiApiKey,
        Models: () => ConnectionSettings.AvailableXaiModels,
        PublishModels: models => ConnectionSettings.SetAvailableXaiModels(models),
        Discover: async (key, token) =>
        {
            using var provider = new XaiImageProvider(key, true, new RhinoImageProcessor());
            return await provider.DiscoverAsync(token);
        },
        Configure: (key, remember, _) => ConnectionSettings.ConfigureXai(key, remember),
        Disable: () => ConnectionSettings.DisableXai(),
        Disconnect: () => ConnectionSettings.DisconnectXai(),
        ResultNote: ImageModelsNote);

    private static CloudConnection Bfl() => new(
        Id: "bfl",
        Name: "BFL",
        Destination: "Cloud · api.bfl.ai",
        ConnectedNote: "Connected. The documented models are in the Model menu; rendering stays off until the reference-upload decision is resolved.",
        KeyHint: "Leave blank to keep the current key. BFL bills this API key's own account.",
        NotCheckedNote: "Not checked this session. The Model menu offers the documented pair: flux-2-pro · curated and flux-2-pro-preview · latest.",
        ConsentRows: ["The connection check verifies the key without an image. Rendering is off until the reference-upload decision is resolved: BFL takes hosted image URLs and no upload surface is documented."],
        BillingRows: null,
        OffersIncludeNames: false,
        RenderingOff: true,
        TestedModelId: BflImageProvider.ModelId,
        TestedLabel: "curated",
        ListedLabel: "latest",
        Allowed: () => ConnectionSettings.BflAllowed,
        Ready: () => ConnectionSettings.BflIsReady,
        RememberSaved: () => ConnectionSettings.BflRememberKey,
        CurrentKey: () => ConnectionSettings.BflApiKey,
        Models: () => ConnectionSettings.AvailableBflModels,
        PublishModels: models => ConnectionSettings.SetAvailableBflModels(models),
        Discover: async (key, token) =>
        {
            using var provider = new BflImageProvider(key, true);
            return await provider.DiscoverAsync(token);
        },
        Configure: (key, remember, _) => ConnectionSettings.ConfigureBfl(key, remember),
        Disable: () => ConnectionSettings.DisableBfl(),
        Disconnect: () => ConnectionSettings.DisconnectBfl(),
        ResultNote: count => $" {count} documented FLUX.2 pro models are in the Model menu. Rendering stays off until the reference-upload decision is resolved.");

    private static string ImageModelsNote(int count) => count == 1
        ? " One image model is available in the Model menu."
        : $" {count} image models are available in the Model menu.";

    // Selects a destination's row and focuses its key field, so the Models tab
    // and Add connection hand the user to the exact connection they asked for.
    internal void Select(string id)
    {
        int index = Array.IndexOf(entryIds, id);
        if (index < 0)
            return;
        list.SelectedIndex = index;
        ShowSelected();
        if (widgetsById.GetValueOrDefault(id) is { } widgets)
            widgets.Secret.Focus();
    }

    private void ShowSelected()
    {
        if (lifetime.IsCancellationRequested)
            return;
        string id = SelectedId is { } selected && panelsById.ContainsKey(selected) ? selected : "local";
        details.Content = panelsById[id];
        if (cloudsById.GetValueOrDefault(id) is { } cloud && widgetsById[id] is { } widgets)
            ShowModels(cloud, widgets);
    }

    private string? SelectedId => list.SelectedIndex >= 0 && list.SelectedIndex < entryIds.Length ? entryIds[list.SelectedIndex] : null;

    private (CloudWidgets widgets, Control panel) BuildCloud(CloudConnection cloud)
    {
        var widgets = new CloudWidgets
        {
            Secret = new PasswordBox { Width = 1 },
            Allow = new CheckBox { Text = "Allow rendering with " + cloud.Name, Checked = cloud.Allowed() },
            Remember = new CheckBox { Text = "Remember API key", Checked = cloud.RememberSaved() },
            Names = cloud.OffersIncludeNames ? new CheckBox
            {
                Text = "Include scene names and descriptions",
                ToolTip = "Includes layer and material names and material descriptions. These may identify the project.",
                Checked = ConnectionSettings.IncludeNames
            } : null,
            Connect = new Button { Text = "Check and save connection" },
            Forget = new Button { Text = "Disconnect and forget key" },
            Status = VisoLoomPanel.CreateLabel(cloud.Ready() ? cloud.ConnectedNote : "Enter an API key to connect " + cloud.Name + "."),
            Supported = VisoLoomPanel.CreateLabel("")
        };
        widgets.Connect.Click += async (_, _) => await CheckConnectionAsync(cloud, widgets);
        widgets.Forget.Click += (_, _) =>
        {
            try
            {
                cloud.Disconnect();
                widgets.Allow.Checked = cloud.Allowed();
                widgets.Remember.Checked = cloud.RememberSaved();
                widgets.Secret.Text = "";
                widgets.Status.Text = "Disconnected. Saved key removed.";
            }
            catch (Exception ex) when (ConnectionSettings.StoreUnavailable(ex))
            { widgets.Status.Text = "Disconnected in VisoLoom. Remove its saved key using this computer's credential manager."; }
        };
        ShowModels(cloud, widgets);
        var layout = new DynamicLayout { Spacing = new Size(8, 8) };
        layout.AddRow(VisoLoomPanel.CreateLabel(cloud.Name, bold: true));
        layout.AddRow(VisoLoomPanel.CreateLabel(cloud.Destination));
        layout.AddRow(widgets.Status);
        var models = new DynamicLayout { Padding = new Padding(8, 4), Spacing = new Size(6, 6) };
        models.AddRow(widgets.Supported);
        layout.AddRow(new Expander { Header = "Supported models", Expanded = false, Content = models });
        layout.AddRow(VisoLoomPanel.CreateLabel("API key"));
        layout.AddRow(widgets.Secret);
        layout.AddRow(VisoLoomPanel.CreateLabel(cloud.KeyHint));
        layout.AddRow(widgets.Allow);
        if (widgets.Names is { } names)
            layout.AddRow(names);
        layout.AddRow(widgets.Remember);
        foreach (string row in cloud.ConsentRows)
            layout.AddRow(VisoLoomPanel.CreateLabel(row));
        if (cloud.BillingRows is { } rows)
        {
            var billing = new DynamicLayout { Padding = new Padding(8, 4), Spacing = new Size(6, 6) };
            foreach (string row in rows)
                billing.AddRow(VisoLoomPanel.CreateLabel(row));
            layout.AddRow(new Expander { Header = "Data sent and billing", Expanded = false, Content = billing });
        }
        layout.AddRow(widgets.Connect);
        layout.AddRow(widgets.Forget);
        return (widgets, layout);
    }

    private void ShowModels(CloudConnection cloud, CloudWidgets widgets)
    {
        var models = cloud.Models();
        widgets.Supported.Text = models.Count == 0 ? cloud.NotCheckedNote :
            string.Join("\n", models.Select(model => "· " + model.DisplayName + " · " +
                (model.Id == cloud.TestedModelId ? cloud.TestedLabel : cloud.ListedLabel)));
    }

    // One shared check flow for every cloud: allow → discover → configure are
    // the provider's own delegates; the widget enable/disable, timeout and error
    // reporting are identical for all of them.
    private async Task CheckConnectionAsync(CloudConnection cloud, CloudWidgets widgets)
    {
        if (widgets.Allow.Checked != true)
        {
            cloud.Disable();
            widgets.Status.Text = cloud.Name + " is disabled.";
            return;
        }
        string key = string.IsNullOrWhiteSpace(widgets.Secret.Text) ? cloud.CurrentKey() : widgets.Secret.Text.Trim();
        bool persist = widgets.Remember.Checked == true;
        bool includeNames = widgets.Names is { } names && names.Checked == true;
        SetBusy(widgets, busy: true);
        widgets.Status.Text = "Checking " + cloud.Name + "…";
        try
        {
            using var ct = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            ct.CancelAfter(TimeSpan.FromSeconds(15));
            var account = await cloud.Discover(key, ct.Token);
            if (lifetime.IsCancellationRequested) return;
            cloud.PublishModels(account.Models);
            widgets.Status.Text = cloud.Configure(key, persist, includeNames) + cloud.ResultNote(account.Models.Count);
            widgets.Secret.Text = "";
            widgets.Allow.Checked = cloud.Allowed();
            widgets.Remember.Checked = cloud.RememberSaved();
        }
        catch (ProviderException ex) { if (!lifetime.IsCancellationRequested) widgets.Status.Text = ex.Message; }
        catch (ArgumentException ex) { if (!lifetime.IsCancellationRequested) widgets.Status.Text = ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        { if (!lifetime.IsCancellationRequested) widgets.Status.Text = "Could not connect to " + cloud.Name + ". Check the network and API key, then try again."; }
        catch (Exception ex) when (ConnectionSettings.StoreUnavailable(ex))
        { if (!lifetime.IsCancellationRequested) widgets.Status.Text = "The key could not be saved securely. Check this computer's credential storage."; }
        finally { if (!lifetime.IsCancellationRequested) SetBusy(widgets, busy: false); }
    }

    private static void SetBusy(CloudWidgets widgets, bool busy)
    {
        widgets.Connect.Enabled = widgets.Forget.Enabled = widgets.Allow.Enabled = widgets.Remember.Enabled = widgets.Secret.Enabled = !busy;
        if (widgets.Names is { } names)
            names.Enabled = !busy;
    }

    private Control BuildLocal(Action<Control>? configureLocal)
    {
        var layout = new DynamicLayout { Spacing = new Size(8, 8) };
        layout.AddRow(VisoLoomPanel.CreateLabel("This computer", bold: true));
        layout.AddRow(localStatus);
        var localAllowed = new CheckBox { Text = "Enable local rendering", Checked = ConnectionSettings.LocalEngineAllowed };
        layout.AddRow(localAllowed);
        layout.AddRow(checkLocal);
        var localSetup = new DynamicLayout { Padding = new Padding(8, 4), Spacing = new Size(6, 6) };
        localSetup.AddRow(VisoLoomPanel.CreateLabel("Local rendering currently needs a configured VisoLoom service and a verified model. Automatic installation is planned."));
        localSetup.AddRow(VisoLoomPanel.CreateLabel("In the development setup, start Test VisoLoom before checking the connection. Platform setup instructions are in DEVELOPMENT.md."));
        localSetup.AddRow(VisoLoomPanel.CreateLabel("Capture test returns the source image unchanged through this service. It can be selected in the Render panel."));
        if (configureLocal is not null)
        {
            var local = new Button { Text = "Manual local connection…" };
            local.Click += (_, _) => configureLocal(this);
            localSetup.AddRow(local);
        }
        layout.AddRow(new Expander { Header = "Local setup", Expanded = false, Content = localSetup });
        localAllowed.CheckedChanged += (_, _) => ConnectionSettings.AllowLocalEngine(localAllowed.Checked == true);
        checkLocal.Click += async (_, _) =>
        {
            if (RhinoDoc.ActiveDoc is not { } document) { localStatus.Text = "Open a Rhino model first."; return; }
            checkLocal.Enabled = false;
            localStatus.Text = "Checking the local connection…";
            try
            {
                var local = await CaptureCoordinator.ForDocument(document.RuntimeSerialNumber).CheckLocalEngineAsync();
                if (lifetime.IsCancellationRequested) return;
                localAllowed.Checked = true;
                ConnectionSettings.AllowLocalEngine(true);
                localStatus.Text = $"Connected. Choose {local.DisplayName} in the Render panel.";
            }
            catch (Exception ex) when (ex is ProviderException or InvalidOperationException)
            { if (!lifetime.IsCancellationRequested) localStatus.Text = ex.Message; }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            { if (!lifetime.IsCancellationRequested) localStatus.Text = "The local service did not respond. Check Local setup below and try again."; }
            finally { if (!lifetime.IsCancellationRequested) checkLocal.Enabled = true; }
        };
        return layout;
    }

    private void AddConnection()
    {
        if (lifetime.IsCancellationRequested) return;
        using var dialog = new Dialog { Title = "VisoLoom · Add connection", ClientSize = new Size(340, 250), Resizable = false };
        var chooser = new ListBox { Height = 118, Width = 1 };
        chooser.Items.Add(new ListItem { Text = "This computer — local engine" });
        foreach (var cloud in providers)
            chooser.Items.Add(new ListItem { Text = cloud.Name + " — Cloud" });
        chooser.SelectedIndex = 1;
        var add = new Button { Text = "Add connection" };
        var cancel = new Button { Text = "Cancel" };
        var layout = new DynamicLayout { Padding = 12, Spacing = new Size(8, 8) };
        layout.AddRow(VisoLoomPanel.CreateLabel("Choose a destination to connect."));
        layout.AddRow(VisoLoomPanel.CreateLabel("The key is entered after the destination is added. A connection check sends no image and never generates."));
        layout.AddRow(chooser);
        layout.AddRow(new TableLayout { Rows = { new TableRow(add, null, cancel) }, Spacing = new Size(8, 0) });
        dialog.Content = layout;
        string? choice = null;
        add.Click += (_, _) => { choice = "add"; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.ShowModal(this);
        if (choice == "add" && chooser.SelectedIndex >= 0)
            Select(entryIds[chooser.SelectedIndex]);
    }

    private void RefreshSummaries()
    {
        if (lifetime.IsCancellationRequested) return;
        list.Items[0].Text = LocalSummary();
        for (int i = 0; i < providers.Count; i++)
            list.Items[i + 1].Text = CloudSummary(providers[i]);
    }

    private string LocalSummary() => coordinator?.LocalModel is { } model
        ? "This computer — checked · " + model.DisplayName
        : ConnectionSettings.LocalEngineAllowed ? "This computer — not checked yet" : "This computer — not enabled";

    private static string CloudSummary(CloudConnection cloud)
    {
        if (cloud.Ready())
        {
            string suffix = cloud.RenderingOff ? " · rendering off" :
                cloud.Models().Count > 0 ? " · " + cloud.Models().Count + " models" : " · not checked yet";
            return cloud.Name + " — connected" + suffix;
        }
        if (!cloud.Allowed())
            return cloud.Name + " — " + (cloud.CurrentKey().Length == 0 ? "not set up" : "disabled");
        return cloud.Name + " — missing API key";
    }

    private void RefreshLocal()
    {
        if (lifetime.IsCancellationRequested) return;
        if (coordinator?.LocalModel is { } model)
            localStatus.Text = "Last checked: " + model.DisplayName + ".";
        else if (ConnectionSettings.LocalEngineAllowed)
            localStatus.Text = "Enabled. Check the connection to confirm an installed model.";
        else
            localStatus.Text = "Connect to a local engine to render with an installed model.";
    }

    private void ConnectionChanged(object? sender, EventArgs e)
    {
        if (lifetime.IsCancellationRequested) return;
        RefreshSummaries();
        RefreshLocal();
        if (SelectedId is { } id && cloudsById.GetValueOrDefault(id) is { } cloud && widgetsById[id] is { } widgets)
            ShowModels(cloud, widgets);
    }

    private void CoordinatorChanged(object? sender, EventArgs e)
    {
        if (lifetime.IsCancellationRequested) return;
        RefreshLocal();
        RefreshSummaries();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ConnectionSettings.Changed -= ConnectionChanged;
            if (coordinator is { } session)
                session.Changed -= CoordinatorChanged;
        }
        base.Dispose(disposing);
    }
}

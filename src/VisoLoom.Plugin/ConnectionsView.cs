using Eto.Drawing;
using Eto.Forms;
using Rhino;
using VisoLoom.Core;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal sealed class ConnectionsView : Panel
{
    private sealed record CloudConnection(
        string Id,
        string Name,
        string KeyHint,
        string[] ConsentRows,
        string[]? BillingRows,
        bool OffersIncludeNames,
        bool RenderingOff,
        Func<bool> Allowed,
        Func<bool> Ready,
        Func<bool> RememberSaved,
        Func<string> CurrentKey,
        Func<IReadOnlyList<ModelCapabilities>> Models,
        Action<IReadOnlyList<ModelCapabilities>> PublishModels,
        Func<string, CancellationToken, Task<ProviderCapabilities>> Discover,
        Func<string, bool, bool, string> Configure,
        Action Disable,
        Action Disconnect);

    private sealed class CloudWidgets
    {
        public PasswordBox Secret { get; } = new() { Width = 1 };
        public CheckBox Allow { get; } = new();
        public CheckBox Remember { get; } = new() { Text = "Remember API key" };
        public CheckBox? Names { get; init; }
        public Button Manage { get; } = new();
        public Button ChangeKey { get; } = new() { Text = "Change API key…" };
        public Button Connect { get; } = new();
        public Button Refresh { get; } = new();
        public Button Forget { get; } = new() { Text = "Disconnect and forget key" };
        public WrappingLabel Summary { get; } = VisoLoomPanel.CreateLabel("");
        public WrappingLabel Status { get; } = VisoLoomPanel.CreateLabel("");
        public WrappingLabel Supported { get; } = VisoLoomPanel.CreateLabel("");
        public Panel Detail { get; } = new() { Visible = false };
        public Panel ModelList { get; } = new();
        public Panel KeyForm { get; } = new();
    }

    private readonly CancellationToken lifetime;
    private readonly List<(CloudConnection Cloud, CloudWidgets Widgets)> rows = [];
    private readonly CaptureCoordinator? coordinator;
    private readonly WrappingLabel localStatus = VisoLoomPanel.CreateLabel("");
    private readonly Button checkLocal = new() { Text = "Check local connection" };

    internal Control LocalSetup { get; }

    internal ConnectionsView(CancellationToken lifetimeToken, Action<Control>? configureLocal = null)
    {
        lifetime = lifetimeToken;
        if (RhinoDoc.ActiveDoc is { } active)
            coordinator = CaptureCoordinator.ForDocument(active.RuntimeSerialNumber);
        LocalSetup = BuildLocal(configureLocal);
        var sections = new List<Control>();
        foreach (var cloud in new[] { OpenAi(), Google(), Xai(), Bfl() })
        {
            if (sections.Count != 0)
                sections.Add(SettingsLayout.Rule());
            var widgets = new CloudWidgets
            {
                Names = cloud.OffersIncludeNames ? new CheckBox
                {
                    Text = "Include scene names and descriptions",
                    ToolTip = "Includes layer and material names and material descriptions. They may identify the project.",
                    Checked = ConnectionSettings.IncludeNames
                } : null
            };
            rows.Add((cloud, widgets));
            sections.Add(BuildCloud(cloud, widgets));
        }
        Content = SettingsLayout.Stack([.. sections]);
        RefreshLocal();
        ConnectionSettings.Changed += ConnectionChanged;
        if (coordinator is { } session)
            session.Changed += CoordinatorChanged;
    }

    private static CloudConnection OpenAi() => new(
        Id: "openai",
        Name: "OpenAI",
        KeyHint: "Leave blank to keep the current key. API billing is separate from your ChatGPT subscription.",
        ConsentRows: ["Saved keys use this computer's secure credential storage.", "Render sends one source image, your prompt and scene guidance. Connection checks send no image."],
        BillingRows: ["Render sends one source image, the representation and prompt, camera and frame, units, clipping planes and material summaries. Layer/material names and material descriptions are included only when enabled.", "Connection checks send no image. Rendering requests one image at medium quality. Stop waiting ends local waiting while OpenAI may still finish and charge the request."],
        OffersIncludeNames: true,
        RenderingOff: false,
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
        Disconnect: () => ConnectionSettings.Disconnect());

    private static CloudConnection Google() => new(
        Id: "google",
        Name: "Google",
        KeyHint: "Leave blank to keep the current key. Google bills this API key's own account.",
        ConsentRows: ["Saved keys use this computer's secure credential storage. Render sends one source image, your prompt and scene guidance."],
        BillingRows: null,
        OffersIncludeNames: false,
        RenderingOff: false,
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
        Disconnect: () => ConnectionSettings.DisconnectGoogle());

    private static CloudConnection Xai() => new(
        Id: "xai",
        Name: "xAI",
        KeyHint: "Leave blank to keep the current key. xAI bills this API key's own account.",
        ConsentRows: ["Saved keys use this computer's secure credential storage. Render sends one source image, your prompt and scene guidance."],
        BillingRows: null,
        OffersIncludeNames: false,
        RenderingOff: false,
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
        Disconnect: () => ConnectionSettings.DisconnectXai());

    private static CloudConnection Bfl() => new(
        Id: "bfl",
        Name: "Black Forest Labs",
        KeyHint: "Leave blank to keep the current key. BFL bills this API key's own account.",
        ConsentRows: ["Check your API key to see the curated models. Rendering with Black Forest Labs is pending."],
        BillingRows: null,
        OffersIncludeNames: false,
        RenderingOff: true,
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
        Disconnect: () => ConnectionSettings.DisconnectBfl());

    private Control BuildCloud(CloudConnection cloud, CloudWidgets widgets)
    {
        widgets.Manage.ID = cloud.Id + "-connection";
        widgets.Secret.ID = cloud.Id + "-key";
        widgets.Detail.ID = cloud.Id + "-details";
        widgets.Allow.Text = "Allow rendering with " + cloud.Name;
        widgets.Allow.Checked = cloud.Allowed();
        widgets.Allow.Visible = !cloud.RenderingOff;
        widgets.Remember.Checked = cloud.RememberSaved();
        widgets.Refresh.Text = cloud.RenderingOff ? "Check connection" : "Refresh models";
        widgets.KeyForm.Content = SettingsLayout.Stack(
            VisoLoomPanel.CreateLabel("API key"), widgets.Secret,
            widgets.Remember, widgets.Allow, widgets.Names ?? (Control)new Panel { Visible = false },
            VisoLoomPanel.CreateLabel(cloud.RenderingOff
                ? "Checking the key sends no image."
                : "A render sends your source image, prompt and scene guidance."),
            SettingsLayout.Actions(widgets.Connect));
        var notes = new List<Control> { VisoLoomPanel.CreateLabel(cloud.KeyHint) };
        foreach (string note in cloud.BillingRows ?? cloud.ConsentRows)
            notes.Add(VisoLoomPanel.CreateLabel(note));
        var connectionDetails = new Expander { Header = "Connection details", Expanded = false, Content = SettingsLayout.Inset(SettingsLayout.Stack([.. notes])) };
        widgets.ModelList.Content = SettingsLayout.Stack(
            VisoLoomPanel.CreateLabel("Image models", bold: true),
            widgets.Supported,
            SettingsLayout.Actions(widgets.Refresh));
        widgets.Detail.Content = SettingsLayout.Inset(SettingsLayout.Stack(
            widgets.ModelList, SettingsLayout.Actions(widgets.ChangeKey), widgets.KeyForm,
            connectionDetails, SettingsLayout.Actions(widgets.Forget), widgets.Status));
        widgets.Status.Visible = false;
        widgets.Manage.Click += (_, _) =>
        {
            bool opening = !widgets.Detail.Visible;
            foreach (var (_, row) in rows)
                row.Detail.Visible = false;
            if (!opening) return;
            widgets.Detail.Visible = true;
            if (!cloud.Ready())
                widgets.Secret.Focus();
        };
        widgets.ChangeKey.Click += (_, _) =>
        {
            widgets.KeyForm.Visible = !widgets.KeyForm.Visible;
            widgets.ChangeKey.Text = widgets.KeyForm.Visible ? "Hide API key" : "Change API key…";
            if (widgets.KeyForm.Visible)
                widgets.Secret.Focus();
        };
        widgets.Connect.Click += async (_, _) => await CheckConnectionAsync(cloud, widgets, refreshOnly: false);
        widgets.Refresh.Click += async (_, _) => await CheckConnectionAsync(cloud, widgets, refreshOnly: true);
        widgets.Forget.Click += (_, _) =>
        {
            try
            {
                cloud.Disconnect();
                SetStatus(widgets, "Disconnected.");
            }
            catch (Exception ex) when (ConnectionSettings.StoreUnavailable(ex))
            {
                SetStatus(widgets, "Disconnected in VisoLoom. Remove the saved key using your computer’s credential manager.");
            }
            ResetKeyForm(cloud, widgets);
        };
        RefreshRow(cloud, widgets);
        ResetKeyForm(cloud, widgets);
        return SettingsLayout.Stack(
            SettingsLayout.Row(SettingsLayout.Caption(cloud.Name, widgets.Summary), widgets.Manage),
            widgets.Detail);
    }

    private static void ResetKeyForm(CloudConnection cloud, CloudWidgets widgets)
    {
        widgets.Secret.Text = "";
        widgets.Allow.Checked = cloud.Allowed();
        widgets.Remember.Checked = cloud.RememberSaved();
        if (widgets.Names is { } names)
            names.Checked = ConnectionSettings.IncludeNames;
        widgets.KeyForm.Visible = !cloud.Ready();
        widgets.ChangeKey.Text = "Change API key…";
    }

    private static void RefreshRow(CloudConnection cloud, CloudWidgets widgets)
    {
        bool ready = cloud.Ready();
        bool wasReady = widgets.ChangeKey.Visible;
        bool hasKey = cloud.CurrentKey().Length != 0;
        widgets.Manage.Text = ready || hasKey ? "Manage…" : "Connect…";
        widgets.Connect.Text = ready ? "Save connection" : "Connect";
        widgets.Summary.Text = ready
            ? (cloud.RememberSaved() ? "Connected · Key saved" : "Connected · This session only") +
                (cloud.RenderingOff ? " · Rendering pending" : "")
            : hasKey ? "Rendering disabled" : "No API key";
        widgets.ModelList.Visible = ready;
        widgets.ChangeKey.Visible = ready;
        if (ready != wasReady)
            ResetKeyForm(cloud, widgets);
        widgets.Forget.Visible = hasKey || cloud.RememberSaved();
        var models = cloud.Models();
        widgets.Supported.Text = models.Count == 0
            ? "Check the connection to see available models."
            : string.Join("\n", models.Select(model => "• " + model.DisplayName));
    }

    private static void SetStatus(CloudWidgets widgets, string message)
    {
        widgets.Status.Text = message;
        widgets.Status.Visible = message.Length != 0;
    }

    private async Task CheckConnectionAsync(CloudConnection cloud, CloudWidgets widgets, bool refreshOnly)
    {
        string currentKey = cloud.CurrentKey();
        bool wasAllowed = cloud.Allowed();
        var previousModels = cloud.Models();
        string key = refreshOnly || string.IsNullOrWhiteSpace(widgets.Secret.Text) ? currentKey : widgets.Secret.Text.Trim();
        if (key.Length == 0)
        {
            SetStatus(widgets, "Enter an API key.");
            widgets.Secret.Focus();
            return;
        }
        if (!refreshOnly && !cloud.RenderingOff && widgets.Allow.Checked != true)
        {
            if (!wasAllowed)
            {
                SetStatus(widgets, "Allow rendering with " + cloud.Name + " to connect.");
                return;
            }
            cloud.Disable();
            ResetKeyForm(cloud, widgets);
            SetStatus(widgets, "Rendering disabled.");
            return;
        }

        SetBusy(widgets, true);
        SetStatus(widgets, "Checking " + cloud.Name + "…");
        try
        {
            using var ct = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            ct.CancelAfter(TimeSpan.FromSeconds(15));
            var account = await cloud.Discover(key, ct.Token);
            if (lifetime.IsCancellationRequested) return;
            if (cloud.CurrentKey() != currentKey || cloud.Allowed() != wasAllowed || !ReferenceEquals(cloud.Models(), previousModels))
            {
                SetStatus(widgets, "The connection changed. Check again.");
                return;
            }
            string notice = refreshOnly
                ? "Model list refreshed."
                : cloud.Configure(key, widgets.Remember.Checked == true, widgets.Names?.Checked == true);
            cloud.PublishModels(account.Models);
            if (!refreshOnly)
                ResetKeyForm(cloud, widgets);
            SetStatus(widgets, account.Models.Count == 0 ? "No supported image models for this account." : notice);
        }
        catch (ProviderException ex) { if (!lifetime.IsCancellationRequested) SetStatus(widgets, ex.Message); }
        catch (ArgumentException ex) { if (!lifetime.IsCancellationRequested) SetStatus(widgets, ex.Message); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        { if (!lifetime.IsCancellationRequested) SetStatus(widgets, "Could not check the connection. Check the network and API key, then try again."); }
        catch (Exception ex) when (ConnectionSettings.StoreUnavailable(ex))
        { if (!lifetime.IsCancellationRequested) SetStatus(widgets, "The key could not be saved securely. Check this computer’s credential storage."); }
        finally { if (!lifetime.IsCancellationRequested) SetBusy(widgets, false); }
    }

    private static void SetBusy(CloudWidgets widgets, bool busy)
    {
        widgets.KeyForm.Enabled = widgets.Refresh.Enabled = widgets.Forget.Enabled = widgets.ChangeKey.Enabled = !busy;
    }

    private Control BuildLocal(Action<Control>? configureLocal)
    {
        var layout = new DynamicLayout { Spacing = new Size(8, 8) };
        layout.AddRow(localStatus);
        var localAllowed = new CheckBox { Text = "Enable local rendering", Checked = ConnectionSettings.LocalEngineAllowed };
        layout.AddRow(localAllowed);
        layout.AddRow(checkLocal);
        var localSetup = new DynamicLayout { Padding = new Padding(8, 4), Spacing = new Size(6, 6) };
        localSetup.AddRow(VisoLoomPanel.CreateLabel("Check the connection to see which installed model the local engine can render with."));
        localSetup.AddRow(VisoLoomPanel.CreateLabel("If the engine is offline, start it and check the connection again."));
        localSetup.AddRow(VisoLoomPanel.CreateLabel("Capture test returns the source image unchanged through this service. It can be selected in the Render panel."));
        if (configureLocal is not null)
        {
            var local = new Button { Text = "Manual local connection…" };
            local.Click += (_, _) => configureLocal(local);
            localSetup.AddRow(local);
        }
        layout.AddRow(new Expander { Header = "Local setup", Expanded = false, Content = localSetup });
        localAllowed.CheckedChanged += (_, _) => ConnectionSettings.AllowLocalEngine(localAllowed.Checked == true);
        checkLocal.Click += async (_, _) =>
        {
            if (coordinator is null) { localStatus.Text = "Open a Rhino model first."; return; }
            checkLocal.Enabled = false;
            localStatus.Text = "Checking the local connection…";
            try
            {
                var local = await coordinator.CheckLocalEngineAsync();
                if (lifetime.IsCancellationRequested) return;
                localAllowed.Checked = true;
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

    private void RefreshLocal()
    {
        if (lifetime.IsCancellationRequested) return;
        localStatus.Text = coordinator?.LocalModel is { } model
            ? "Last checked: " + model.DisplayName
            : ConnectionSettings.LocalEngineAllowed ? "Enabled · Check connection" : "Local rendering is off.";
    }

    private void ConnectionChanged(object? sender, EventArgs e)
    {
        if (lifetime.IsCancellationRequested) return;
        foreach (var (cloud, widgets) in rows)
            RefreshRow(cloud, widgets);
        RefreshLocal();
    }

    private void CoordinatorChanged(object? sender, EventArgs e) => RefreshLocal();

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

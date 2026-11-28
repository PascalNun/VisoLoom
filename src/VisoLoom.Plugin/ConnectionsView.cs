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
        string RowNote,
        string[] TransmitRows,
        bool OffersIncludeNames,
        bool RenderingOff,
        Func<bool> Ready,
        Func<string> CurrentKey,
        Func<IReadOnlyList<ModelCapabilities>> Models,
        Action<IReadOnlyList<ModelCapabilities>> PublishModels,
        Func<string, CancellationToken, Task<ProviderCapabilities>> Discover,
        Func<string, bool, string> Configure,
        Action Disconnect);

    private sealed class CloudWidgets
    {
        public PasswordBox Secret { get; } = new() { Width = 1 };
        public CheckBox? Names { get; init; }
        public Button Manage { get; } = new();
        public Button ChangeKey { get; } = new() { Text = "Change API key…" };
        public Button Connect { get; } = new();
        public Button Refresh { get; } = new() { Text = "Refresh models" };
        public Button Forget { get; } = new() { Text = "Disconnect and forget key" };
        public WrappingLabel KeyLabel { get; } = VisoLoomPanel.CreateLabel("");
        public WrappingLabel KeyHint { get; } = SettingsLayout.Secondary("");
        public WrappingLabel Summary { get; } = VisoLoomPanel.CreateLabel("");
        public WrappingLabel Status { get; } = VisoLoomPanel.CreateLabel("");
        public WrappingLabel Disclosure { get; } = SettingsLayout.Secondary("");
        public WrappingLabel Supported { get; } = VisoLoomPanel.CreateLabel("");
        public Panel Detail { get; } = new() { Visible = false };
        public Panel ModelList { get; } = new();
        public Panel KeyForm { get; } = new();
    }

    private readonly CancellationToken lifetime;
    private readonly List<(CloudConnection Cloud, CloudWidgets Widgets)> rows = [];

    internal ConnectionsView(CancellationToken lifetimeToken)
    {
        lifetime = lifetimeToken;
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
        // The recorded first screen opens the first provider until one exists.
        if (rows.Count != 0 && rows.All(row => !row.Cloud.Ready()))
            rows[0].Widgets.Detail.Visible = true;
        ConnectionSettings.Changed += ConnectionChanged;
    }

    private static CloudConnection OpenAi() => new(
        Id: "openai",
        Name: "OpenAI",
        KeyHint: "Leave blank to keep the current key. API billing is separate from your ChatGPT subscription.",
        RowNote: "A render sends one source image, your prompt and scene guidance.",
        TransmitRows:
        [
            "Render sends one source image, the representation and prompt, camera and frame, units, clipping planes and material summaries. Layer/material names and material descriptions are included only when enabled.",
            "Connection checks send no image. Rendering requests one image at medium quality. Stop waiting ends local waiting while OpenAI may still finish and charge the request."
        ],
        OffersIncludeNames: true,
        RenderingOff: false,
        Ready: () => ConnectionSettings.IsReady,
        CurrentKey: () => ConnectionSettings.ApiKey,
        Models: () => ConnectionSettings.AvailableOpenAiModels,
        PublishModels: models => ConnectionSettings.SetAvailableOpenAiModels(models),
        Discover: async (key, token) =>
        {
            using var provider = new OpenAiImageProvider(key, true, new RhinoImageProcessor());
            return await provider.DiscoverAsync(token);
        },
        Configure: (key, includeNames) => ConnectionSettings.Configure(key, includeNames),
        Disconnect: () => ConnectionSettings.Disconnect());

    private static CloudConnection Google() => new(
        Id: "google",
        Name: "Google",
        KeyHint: "Leave blank to keep the current key. Google bills this API key's own account.",
        RowNote: "A render sends one source image, your prompt and scene guidance.",
        TransmitRows: [],
        OffersIncludeNames: false,
        RenderingOff: false,
        Ready: () => ConnectionSettings.GoogleIsReady,
        CurrentKey: () => ConnectionSettings.GoogleApiKey,
        Models: () => ConnectionSettings.AvailableGoogleModels,
        PublishModels: models => ConnectionSettings.SetAvailableGoogleModels(models),
        Discover: async (key, token) =>
        {
            using var provider = new GoogleImageProvider(key, true, new RhinoImageProcessor());
            return await provider.DiscoverAsync(token);
        },
        Configure: (key, _) => ConnectionSettings.ConfigureGoogle(key),
        Disconnect: () => ConnectionSettings.DisconnectGoogle());

    private static CloudConnection Xai() => new(
        Id: "xai",
        Name: "xAI",
        KeyHint: "Leave blank to keep the current key. xAI bills this API key's own account.",
        RowNote: "A render sends one source image, your prompt and scene guidance.",
        TransmitRows: [],
        OffersIncludeNames: false,
        RenderingOff: false,
        Ready: () => ConnectionSettings.XaiIsReady,
        CurrentKey: () => ConnectionSettings.XaiApiKey,
        Models: () => ConnectionSettings.AvailableXaiModels,
        PublishModels: models => ConnectionSettings.SetAvailableXaiModels(models),
        Discover: async (key, token) =>
        {
            using var provider = new XaiImageProvider(key, true, new RhinoImageProcessor());
            return await provider.DiscoverAsync(token);
        },
        Configure: (key, _) => ConnectionSettings.ConfigureXai(key),
        Disconnect: () => ConnectionSettings.DisconnectXai());

    private static CloudConnection Bfl() => new(
        Id: "bfl",
        Name: "Black Forest Labs",
        KeyHint: "Leave blank to keep the current key. BFL bills this API key's own account.",
        RowNote: "The model list is curated. BFL bills this API key's own account.",
        TransmitRows: [],
        OffersIncludeNames: false,
        RenderingOff: false,
        Ready: () => ConnectionSettings.BflIsReady,
        CurrentKey: () => ConnectionSettings.BflApiKey,
        Models: () => ConnectionSettings.AvailableBflModels,
        PublishModels: models => ConnectionSettings.SetAvailableBflModels(models),
        Discover: async (key, token) =>
        {
            using var provider = new BflImageProvider(key, true, new RhinoImageProcessor());
            return await provider.DiscoverAsync(token);
        },
        Configure: (key, _) => ConnectionSettings.ConfigureBfl(key),
        Disconnect: () => ConnectionSettings.DisconnectBfl());

    private Control BuildCloud(CloudConnection cloud, CloudWidgets widgets)
    {
        widgets.Manage.ID = cloud.Id + "-connection";
        widgets.Secret.ID = cloud.Id + "-key";
        widgets.Detail.ID = cloud.Id + "-details";
        widgets.Refresh.Visible = !cloud.RenderingOff;
        widgets.Disclosure.Text = cloud.RowNote;
        widgets.KeyForm.Content = SettingsLayout.Stack(
            widgets.KeyLabel,
            widgets.Secret,
            widgets.KeyHint,
            widgets.Names ?? (Control)new Panel { Visible = false },
            SettingsLayout.Actions(widgets.Connect));
        var transmit = new List<Control>();
        foreach (string note in cloud.TransmitRows)
            transmit.Add(SettingsLayout.Secondary(note));
        var modelBlock = new List<Control>
        {
            VisoLoomPanel.CreateLabel("Image models", bold: true),
            widgets.Supported,
            SettingsLayout.Actions(widgets.Refresh)
        };
        modelBlock.AddRange(transmit);
        widgets.ModelList.Content = SettingsLayout.Stack([.. modelBlock]);
        widgets.Detail.Content = SettingsLayout.Inset(SettingsLayout.Stack(widgets.Disclosure,
            widgets.ModelList, SettingsLayout.Actions(widgets.ChangeKey), widgets.KeyForm,
            SettingsLayout.Actions(widgets.Forget), widgets.Status));
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
        widgets.Secret.TextChanged += (_, _) => widgets.Connect.Enabled = widgets.Secret.Text.Length != 0;
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
                SetStatus(widgets, "Disconnected in VisoLoom. Remove the saved key using your computer's credential manager.");
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
        widgets.KeyForm.Visible = !cloud.Ready();
        widgets.ChangeKey.Text = "Change API key…";
        if (widgets.Names is { } names)
            names.Checked = ConnectionSettings.IncludeNames;
        RefreshKeyCopy(cloud, widgets);
        widgets.Connect.Enabled = false;
    }

    private static void RefreshKeyCopy(CloudConnection cloud, CloudWidgets widgets)
    {
        bool ready = cloud.Ready();
        widgets.KeyLabel.Text = ready ? "Replacement API key" : cloud.Name + " API key";
        widgets.KeyHint.Text = ready
            ? "Paste a new API key. Leave blank to keep the current key."
            : "Paste your " + cloud.Name + " API key. It is kept in this computer's credential store.";
        widgets.Connect.Text = ready ? "Replace key" : "Connect";
    }

    private static void RefreshRow(CloudConnection cloud, CloudWidgets widgets)
    {
        bool ready = cloud.Ready();
        bool wasReady = widgets.ChangeKey.Visible;
        var models = cloud.Models();
        bool checkedConnection = ready && models.Count != 0;
        widgets.Manage.Text = ready ? "Manage…" : "Connect…";
        widgets.Summary.Text = ready ? "Connected · Key saved" : "No API key";
        SettingsLayout.ConnectionStatus(widgets.Summary, ready);
        widgets.ModelList.Visible = ready;
        widgets.ChangeKey.Visible = ready;
        if (ready != wasReady)
            ResetKeyForm(cloud, widgets);
        else
            RefreshKeyCopy(cloud, widgets);
        widgets.Forget.Visible = ready;
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
        string key = refreshOnly || string.IsNullOrWhiteSpace(widgets.Secret.Text) ? currentKey : widgets.Secret.Text.Trim();
        if (key.Length == 0)
        {
            SetStatus(widgets, "Enter an API key.");
            widgets.Secret.Focus();
            return;
        }

        SetBusy(widgets, true);
        SetStatus(widgets, "Checking available models…");
        try
        {
            using var ct = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            ct.CancelAfter(TimeSpan.FromSeconds(15));
            var account = await cloud.Discover(key, ct.Token);
            if (lifetime.IsCancellationRequested) return;
            if (cloud.CurrentKey() != currentKey)
            {
                SetStatus(widgets, "The connection changed. Check again.");
                return;
            }
            string notice = refreshOnly
                ? "Model list up to date."
                : cloud.Configure(key, widgets.Names?.Checked == true);
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
        { if (!lifetime.IsCancellationRequested) SetStatus(widgets, "The key could not be saved securely. Check this computer's credential storage."); }
        finally { if (!lifetime.IsCancellationRequested) SetBusy(widgets, false); }
    }

    private static void SetBusy(CloudWidgets widgets, bool busy)
    {
        widgets.KeyForm.Enabled = widgets.Refresh.Enabled = widgets.Forget.Enabled = widgets.ChangeKey.Enabled = widgets.Manage.Enabled = !busy;
    }

    private void ConnectionChanged(object? sender, EventArgs e)
    {
        if (lifetime.IsCancellationRequested) return;
        foreach (var (cloud, widgets) in rows)
            RefreshRow(cloud, widgets);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ConnectionSettings.Changed -= ConnectionChanged;
        base.Dispose(disposing);
    }
}

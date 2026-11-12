using Eto.Drawing;
using Eto.Forms;
using Rhino.UI;
using Rhino;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal sealed class ConnectionSettingsPage() : OptionsDialogPage("VisoLoom")
{
    private ConnectionSettingsControl? control;
    public override object PageControl => control ??= new();
}

internal sealed class ConnectionSettingsControl : Panel
{
    private readonly PasswordBox secret = new() { Width = 1 };
    private readonly CheckBox cloud = new() { Text = "Allow rendering with OpenAI" };
    private readonly CheckBox names = new() { Text = "Include layer and material names" };
    private readonly CheckBox remember = new() { Text = "Remember API key" };
    private readonly Button connect = new() { Text = "Check and save connection" };
    private readonly Button forget = new() { Text = "Disconnect and forget key" };
    private readonly WrappingLabel status = VisoLoomPanel.CreateLabel("");
    private readonly TabControl tabs = new();
    private readonly CancellationTokenSource lifetime = new();

    public ConnectionSettingsControl(Action<Control>? configureLocal = null, Control? imageSaving = null)
    {
        cloud.Checked = ConnectionSettings.CloudAllowed;
        names.Checked = ConnectionSettings.IncludeNames;
        remember.Checked = ConnectionSettings.RememberKey;
        var models = new LocalModelsControl(lifetime.Token, OpenCloudConnection, () => tabs.SelectedIndex = 1);
        var connections = new DynamicLayout { Spacing = new Size(8, 8) };
        connections.AddRow(VisoLoomPanel.CreateLabel("This computer", bold: true));
        var localStatus = VisoLoomPanel.CreateLabel("Connect to a local engine to render with an installed model.");
        var localAllowed = new CheckBox { Text = "Enable local rendering", Checked = ConnectionSettings.LocalEngineAllowed };
        var checkLocal = new Button { Text = "Check local connection" };
        connections.AddRow(localStatus);
        connections.AddRow(localAllowed);
        connections.AddRow(checkLocal);
        checkLocal.Enabled = localAllowed.Enabled = ModelCatalogue.SupportsCurrentLocalEngine;
        if (!ModelCatalogue.SupportsCurrentLocalEngine) localStatus.Text = ModelCatalogue.ComputerSummary;
        localAllowed.CheckedChanged += (_, _) => ConnectionSettings.AllowLocalEngine(localAllowed.Checked == true);
        checkLocal.Click += async (_, _) =>
        {
            if (RhinoDoc.ActiveDoc is not { } document) { localStatus.Text = "Open a Rhino model first."; return; }
            checkLocal.Enabled = false;
            localStatus.Text = "Checking the local connection…";
            try
            {
                await CaptureCoordinator.ForDocument(document.RuntimeSerialNumber).CheckLocalEngineAsync();
                if (lifetime.IsCancellationRequested) return;
                localAllowed.Checked = true;
                ConnectionSettings.AllowLocalEngine(true);
                localStatus.Text = "Connected. Choose FLUX.2 klein 4B in the Render panel.";
            }
            catch (Exception ex) when (ex is ProviderException or InvalidOperationException or IOException or OperationCanceledException)
            { if (!lifetime.IsCancellationRequested) localStatus.Text = "Could not connect to a local model. Check the model in Models, then follow Local setup below."; }
            finally { if (!lifetime.IsCancellationRequested) checkLocal.Enabled = true; }
        };
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
        connections.AddRow(new Expander { Header = "Local setup", Expanded = false, Content = localSetup });
        connections.AddRow(new Label { Height = 6 });
        connections.AddRow(VisoLoomPanel.CreateLabel("OpenAI · Cloud", bold: true));
        connections.AddRow(VisoLoomPanel.CreateLabel("API key"));
        connections.AddRow(secret);
        connections.AddRow(VisoLoomPanel.CreateLabel("Leave blank to keep the current key. API billing is separate from your ChatGPT subscription."));
        connections.AddRow(cloud);
        connections.AddRow(names);
        connections.AddRow(remember);
        connections.AddRow(VisoLoomPanel.CreateLabel("Saved keys use this computer's secure credential storage."));
        var disclosure = new DynamicLayout { Padding = new Padding(8, 4), Spacing = new Size(6, 6) };
        disclosure.AddRow(VisoLoomPanel.CreateLabel("Render sends one source image, the representation and prompt, camera and frame, units, clipping planes and material summaries. Layer and material names are included only when enabled."));
        disclosure.AddRow(VisoLoomPanel.CreateLabel("Connection checks send no image. Rendering requests one image at medium quality. Stop waiting ends local waiting while OpenAI may still finish and charge the request."));
        connections.AddRow(new Expander { Header = "Data sent and billing", Expanded = false, Content = disclosure });
        connections.AddRow(connect);
        connections.AddRow(forget);
        connections.AddRow(status);
        connections.Add(null);
        tabs.Pages.Add(Page("Models", models));
        tabs.Pages.Add(Page("Connections", connections));
        tabs.Pages.Add(Page("Image saving", imageSaving ?? ImageSavingSettings.CreateControl()));
        Content = tabs;
        connect.Click += async (_, _) => await ConnectAsync();
        forget.Click += (_, _) =>
        {
            try
            { ConnectionSettings.Disconnect(); cloud.Checked = false; secret.Text = ""; status.Text = "Disconnected. Saved key removed."; }
            catch (Exception ex) when (ConnectionSettings.StoreUnavailable(ex)) { status.Text = "Disconnected in VisoLoom. Remove its saved key using this computer's credential manager."; }
        };
        status.Text = ConnectionSettings.IsReady ? "Connected. Choose OpenAI in the Render panel." : "Enter an API key to connect OpenAI.";
        Load += (_, _) =>
        {
            cloud.Checked = ConnectionSettings.CloudAllowed;
            names.Checked = ConnectionSettings.IncludeNames;
            remember.Checked = ConnectionSettings.RememberKey;
        };
    }

    private static TabPage Page(string title, Control content) => new()
    {
        Text = title,
        Content = new Scrollable { Content = new Panel { Padding = 12, Content = content }, Border = BorderType.None, ExpandContentWidth = true }
    };

    private void OpenCloudConnection()
    {
        tabs.SelectedIndex = 1;
        secret.Focus();
    }

    private async Task ConnectAsync()
    {
        if (cloud.Checked != true)
        {
            ConnectionSettings.DisableCloud();
            status.Text = "OpenAI is disabled.";
            return;
        }
        string key = string.IsNullOrWhiteSpace(secret.Text) ? ConnectionSettings.ApiKey : secret.Text.Trim();
        bool includeNames = names.Checked == true, persist = remember.Checked == true;
        connect.Enabled = forget.Enabled = cloud.Enabled = names.Enabled = remember.Enabled = secret.Enabled = false;
        status.Text = "Checking OpenAI…";
        try
        {
            using var ct = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            ct.CancelAfter(TimeSpan.FromSeconds(15));
            using var provider = new OpenAiImageProvider(key, true, new RhinoImageProcessor());
            await provider.DiscoverAsync(ct.Token);
            if (lifetime.IsCancellationRequested) return;
            status.Text = ConnectionSettings.Configure(key, includeNames, persist);
            secret.Text = "";
            remember.Checked = ConnectionSettings.RememberKey;
        }
        catch (ProviderException ex) { if (!lifetime.IsCancellationRequested) status.Text = ex.Message; }
        catch (ArgumentException ex) { if (!lifetime.IsCancellationRequested) status.Text = ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { if (!lifetime.IsCancellationRequested) status.Text = "Could not connect to OpenAI. Check the network and API key, then try again."; }
        catch (Exception ex) when (ConnectionSettings.StoreUnavailable(ex)) { if (!lifetime.IsCancellationRequested) status.Text = "The key could not be saved securely. Check this computer's credential storage."; }
        finally { if (!lifetime.IsCancellationRequested) connect.Enabled = forget.Enabled = cloud.Enabled = names.Enabled = remember.Enabled = secret.Enabled = true; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { lifetime.Cancel(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }

    public static void ShowSettings(Control owner, Action<Control>? configureLocal = null)
    {
        using var dialog = new Dialog { Title = "VisoLoom · Settings", ClientSize = new Size(480, 600), MinimumSize = new Size(360, 400), Resizable = true };
        var done = new Button { Text = "Done" };
        var layout = new DynamicLayout { Padding = 8, Spacing = new Size(8, 8) };
        layout.Add(new ConnectionSettingsControl(configureLocal), yscale: true);
        layout.AddRow(done);
        dialog.Content = layout;
        dialog.AbortButton = done;
        done.Click += (_, _) => dialog.Close();
        dialog.ShowModal(owner);
    }
}

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
    private readonly PasswordBox secret = new();
    private readonly CheckBox cloud = new() { Text = "Allow rendering with OpenAI" };
    private readonly CheckBox names = new() { Text = "Include layer and material names/descriptions" };
    private readonly CheckBox remember = new() { Text = "Remember API key" };
    private readonly Button connect = new() { Text = "Check and save connection" };
    private readonly Button forget = new() { Text = "Disconnect and forget key" };
    private readonly WrappingLabel status = VisoLoomPanel.CreateLabel("");

    public ConnectionSettingsControl(Action<Control>? configureLocal = null)
    {
        Padding = 12;
        cloud.Checked = ConnectionSettings.CloudAllowed;
        names.Checked = ConnectionSettings.IncludeNames;
        remember.Checked = ConnectionSettings.RememberKey;
        var layout = new DynamicLayout { Spacing = new Size(8, 8) };
        var saving = new Button { Text = "Image saving…" };
        saving.Click += (_, _) => ImageSavingSettings.Show(this);
        layout.AddRow(saving);
        layout.AddRow(VisoLoomPanel.CreateLabel("Connections", bold: true));
        layout.AddRow(VisoLoomPanel.CreateLabel("This computer · capture test", bold: true));
        layout.AddRow(VisoLoomPanel.CreateLabel("The test launcher connects automatically and returns your capture unchanged."));
        var localStatus = VisoLoomPanel.CreateLabel("Use a prepared local model through the test launcher. No account or API key is needed.");
        var localAllowed = new CheckBox { Text = "Use the prepared model on this computer", Checked = ConnectionSettings.LocalEngineAllowed };
        var checkLocal = new Button { Text = "Check local model" };
        layout.AddRow(VisoLoomPanel.CreateLabel("This computer · FLUX.2 klein 4B", bold: true));
        layout.AddRow(localStatus);
        layout.AddRow(localAllowed);
        layout.AddRow(checkLocal);
        localAllowed.CheckedChanged += (_, _) => ConnectionSettings.AllowLocalEngine(localAllowed.Checked == true);
        checkLocal.Click += async (_, _) =>
        {
            if (RhinoDoc.ActiveDoc is not { } document) { localStatus.Text = "Open a Rhino model first."; return; }
            checkLocal.Enabled = false;
            localStatus.Text = "Checking the local connection and prepared model; no image is sent…";
            try
            {
                await CaptureCoordinator.ForDocument(document.RuntimeSerialNumber).CheckLocalEngineAsync();
                localAllowed.Checked = true;
                ConnectionSettings.AllowLocalEngine(true);
                localStatus.Text = "Ready. Choose FLUX.2 klein 4B in the panel's Model menu. Rendering stays on this computer.";
            }
            catch (Exception ex) when (ex is ProviderException or InvalidOperationException or IOException or OperationCanceledException)
            { localStatus.Text = "No ready local model found. Start the configured Test VisoLoom launcher, then check again. Model installation is still a development setup."; }
            finally { checkLocal.Enabled = true; }
        };
        if (configureLocal is not null)
        {
            var local = new Button { Text = "Manual local connection…" };
            local.Click += (_, _) => configureLocal(this);
            layout.AddRow(local);
        }
        var models = new Button { Text = "Model library…" };
        models.Click += (_, _) => LocalModelsControl.ShowLibrary(this);
        layout.AddRow(models);
        layout.AddRow(VisoLoomPanel.CreateLabel("OpenAI · Sunburst image editing", bold: true));
        layout.AddRow(VisoLoomPanel.CreateLabel("API key"));
        layout.AddRow(secret);
        layout.AddRow(VisoLoomPanel.CreateLabel("Leave blank to keep the current key. API billing is separate from your ChatGPT subscription."));
        layout.AddRow(cloud);
        layout.AddRow(names);
        layout.AddRow(remember);
        layout.AddRow(VisoLoomPanel.CreateLabel("Remember uses macOS Keychain or Windows Credential Manager, with an explicit session-only fallback if saving is unavailable."));
        layout.AddRow(VisoLoomPanel.CreateLabel("Sent on Render: one captured PNG, preset/prompt, camera and frame, units, up to 16 active clipping planes and 32 material summaries. Names are optional and shortened to 120 characters. Rhino files, geometry and texture files stay on this computer."));
        layout.AddRow(VisoLoomPanel.CreateLabel("Connection checks send no image and do not generate a billed image. Rendering requests one image at medium quality. Stopping waits locally; OpenAI may still finish and charge the request."));
        layout.AddRow(connect);
        layout.AddRow(forget);
        layout.AddRow(status);
        layout.Add(null);
        Content = new Scrollable { Content = layout, Border = BorderType.None, ExpandContentWidth = true };
        connect.Click += async (_, _) => await ConnectAsync();
        forget.Click += (_, _) =>
        {
            try
            { ConnectionSettings.Disconnect(); cloud.Checked = false; secret.Text = ""; status.Text = "Disconnected. Saved key removed."; }
            catch (Exception ex) when (ConnectionSettings.StoreUnavailable(ex)) { status.Text = "Disconnected in VisoLoom. The OS could not remove the saved key; remove its VisoLoom entry in your credential manager."; }
        };
        status.Text = ConnectionSettings.IsReady ? "Connection configured. Choose OpenAI in the panel's Model menu." : "OpenAI is not configured. Enter your API key to connect this cloud model.";
        Load += (_, _) =>
        {
            cloud.Checked = ConnectionSettings.CloudAllowed;
            names.Checked = ConnectionSettings.IncludeNames;
            remember.Checked = ConnectionSettings.RememberKey;
        };
    }

    private async Task ConnectAsync()
    {
        if (cloud.Checked != true)
        {
            ConnectionSettings.DisableCloud();
            status.Text = "OpenAI is disabled. No image will be sent; the key is unchanged.";
            return;
        }
        string key = string.IsNullOrWhiteSpace(secret.Text) ? ConnectionSettings.ApiKey : secret.Text.Trim();
        bool includeNames = names.Checked == true, persist = remember.Checked == true;
        connect.Enabled = forget.Enabled = cloud.Enabled = names.Enabled = remember.Enabled = secret.Enabled = false;
        status.Text = "Checking the configured image model; no image is sent…";
        try
        {
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var provider = new OpenAiImageProvider(key, true, new RhinoImageProcessor());
            await provider.DiscoverAsync(ct.Token);
            status.Text = ConnectionSettings.Configure(key, includeNames, persist);
            secret.Text = "";
            remember.Checked = ConnectionSettings.RememberKey;
        }
        catch (ProviderException ex) { status.Text = ex.Message; }
        catch (ArgumentException ex) { status.Text = ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { status.Text = "Could not check OpenAI. Check the network and API key, then try again."; }
        catch (Exception ex) when (ConnectionSettings.StoreUnavailable(ex)) { status.Text = "The OS could not update the saved credential. No plaintext copy was made."; }
        finally { connect.Enabled = forget.Enabled = cloud.Enabled = names.Enabled = remember.Enabled = secret.Enabled = true; }
    }

    public static void ShowSettings(Control owner, Action<Control>? configureLocal = null)
    {
        using var dialog = new Dialog { Title = "VisoLoom · Settings", ClientSize = new Size(460, 600), Resizable = true };
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

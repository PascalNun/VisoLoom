using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Eto.Forms;
using VisoLoom.Protocol;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal static class ServerSettings
{
    public delegate Task<IReadOnlyList<string>> Discover(string endpoint, string token, CancellationToken ct);

    public static Control CreateControl(Discover? discover = null) => new ServerSection(discover);

    private sealed class ServerSection : Panel
    {
        private readonly Discover discover;
        private readonly CancellationTokenSource lifetime = new();
        private readonly WrappingLabel name = VisoLoomPanel.CreateLabel("");
        private readonly WrappingLabel status = SettingsLayout.Secondary("");
        private readonly Button toggle = new() { Text = "Add server…" };
        private readonly TextBox address = new() { Width = 1 };
        private readonly PasswordBox token = new() { Width = 1 };
        private readonly Button check = new() { Text = "Check connection", Enabled = false };
        private readonly Button remove = new() { Text = "Remove server", Visible = false };
        private readonly WrappingLabel feedback = VisoLoomPanel.CreateLabel("");
        private bool busy;

        public ServerSection(Discover? discover)
        {
            this.discover = discover ?? DiscoverAsync;
            feedback.Visible = false;
            var form = SettingsLayout.Inset(SettingsLayout.Stack(
                VisoLoomPanel.CreateLabel("Server address"), address,
                SettingsLayout.Secondary("A VisoLoom server on another machine, for example https://studio.example."),
                VisoLoomPanel.CreateLabel("Server access token, if required"), token,
                SettingsLayout.Secondary("Only if the server's operator gave you one. It is kept in this computer's credential store."),
                SettingsLayout.Actions(check, remove), feedback));
            form.Visible = false;
            Content = SettingsLayout.Stack(
                SettingsLayout.Row(SettingsLayout.Stack(name, status), toggle), form);
            toggle.Click += (_, _) =>
            {
                form.Visible = !form.Visible;
                if (form.Visible)
                    address.Focus();
            };
            address.TextChanged += (_, _) => { if (!busy) check.Enabled = address.Text.Trim().Length != 0; };
            check.Click += async (_, _) => await CheckAsync();
            remove.Click += (_, _) => ConnectionSettings.DisconnectServer();
            ConnectionSettings.Changed += ConnectionChanged;
            Reload();
        }

        private void ConnectionChanged(object? sender, EventArgs e)
        {
            if (!lifetime.IsCancellationRequested)
                Reload();
        }

        private async Task CheckAsync()
        {
            if (busy || lifetime.IsCancellationRequested) return;
            string entered = address.Text.Trim().TrimEnd('/');
            if (entered.Length == 0)
            {
                SetFeedback("Enter the server address.");
                address.Focus();
                return;
            }
            try
            {
                EndpointPolicy.RequireUserServerEndpoint(new Uri(entered));
            }
            catch (Exception ex) when (ex is UriFormatException or ArgumentException)
            {
                SetFeedback(ex is ArgumentException argument ? argument.Message : "Enter the server address, for example https://studio.example.");
                return;
            }
            busy = true;
            check.Enabled = remove.Enabled = token.Enabled = address.Enabled = toggle.Enabled = false;
            SetFeedback("Checking available models…");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var models = await discover(entered, token.Text.Trim(), timeout.Token);
                if (lifetime.IsCancellationRequested) return;
                ConnectionSettings.SetServerToken(token.Text.Trim());
                ConnectionSettings.ConfigureServer(entered);
                ConnectionSettings.SetServerChecked(models.Count == 0 ? "" : models[0]);
                SetFeedback(models.Count == 0 ? "The server offers no image models to use." : "Model list up to date.");
            }
            catch (ProviderException ex) { if (!lifetime.IsCancellationRequested) SetFeedback(ex.Message); }
            catch (ArgumentException ex) { if (!lifetime.IsCancellationRequested) SetFeedback(ex.Message); }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            { if (!lifetime.IsCancellationRequested) SetFeedback("Could not check the connection. The server did not answer."); }
            catch (OperationCanceledException)
            { if (!lifetime.IsCancellationRequested) SetFeedback("The check timed out. Try again with the server reachable."); }
            catch (Exception ex) when (ConnectionSettings.StoreUnavailable(ex))
            { if (!lifetime.IsCancellationRequested) SetFeedback("The access token could not be saved securely. Check this computer's credential storage."); }
            finally
            {
                busy = false;
                if (!lifetime.IsCancellationRequested)
                {
                    check.Enabled = address.Text.Trim().Length != 0;
                    remove.Enabled = token.Enabled = address.Enabled = toggle.Enabled = true;
                }
            }
        }

        private void Reload()
        {
            bool added = ConnectionSettings.ServerAddress.Length != 0;
            bool connected = ConnectionSettings.ServerConnected;
            name.Text = added ? new Uri(ConnectionSettings.ServerAddress).Host : "No server added";
            status.Text = connected
                ? "Connected · " + ConnectionSettings.ServerModel
                : added ? "Saved · Check the connection." : "Use a model running on another machine";
            SettingsLayout.ConnectionStatus(status, connected);
            toggle.Text = added ? "Manage…" : "Add server…";
            remove.Visible = added;
            if (!added)
            {
                address.Text = "";
                token.Text = "";
                check.Enabled = false;
            }
            else if (string.IsNullOrWhiteSpace(address.Text))
            {
                address.Text = ConnectionSettings.ServerAddress;
                check.Enabled = true;
            }
        }

        private void SetFeedback(string message)
        {
            feedback.Text = message;
            feedback.Visible = message.Length != 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ConnectionSettings.Changed -= ConnectionChanged;
                lifetime.Cancel();
                lifetime.Dispose();
            }
            base.Dispose(disposing);
        }

        private static async Task<IReadOnlyList<string>> DiscoverAsync(string endpoint, string token, CancellationToken ct)
        {
            using var client = new HttpClient { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromSeconds(20) };
            if (token.Length != 0)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.GetAsync("api/v1/capabilities", ct);
            if (!response.IsSuccessStatusCode)
                throw new ProviderException("serverCheckFailed", response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "The server did not accept this access token."
                    : "The server did not answer with its model list.");
            var body = await response.Content.ReadAsStringAsync(ct);
            var capabilities = JsonSerializer.Deserialize<CapabilitiesResponse>(body, WireJson.Options)
                ?? throw new ProviderException("serverCheckFailed", "The server did not answer with its model list.");
            if (capabilities.ProtocolVersion != "1.0")
                throw new ProviderException("serverCheckFailed", "The server runs a different VisoLoom protocol version.");
            return capabilities.Models.Select(model => model.DisplayName).ToArray();
        }
    }
}

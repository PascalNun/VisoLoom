using System.Reflection;
using System.Text.Json;
using Eto.Drawing;
using Eto.Forms;
using VisoLoom.Plugin;
using VisoLoom.Providers;

namespace VisoLoom.NativeChecks;

public static class SettingsCheck
{
    public static void Run(string report)
    {
        // Only the isolated fixture assembly sees this pretend connection. No
        // request or saved-key operation is part of the walkthrough.
        const BindingFlags stateFlags = BindingFlags.Static | BindingFlags.NonPublic;
        var connectionType = typeof(ConnectionSettings);
        connectionType.GetField("apiKey", stateFlags)!.SetValue(null, "fixture-key");
        connectionType.GetProperty(nameof(ConnectionSettings.CloudAllowed))!.SetValue(null, true);
        connectionType.GetProperty(nameof(ConnectionSettings.RememberKey))!.SetValue(null, true);
        ConnectionSettings.SetAvailableOpenAiModels([OpenAiImageProvider.Model]);

        bool savingChanged = false;
        using var settings = new ConnectionSettingsControl(imageSaving: ImageSavingSettings.CreateControl(null, _ => savingChanged = true));
        using var dialog = new Dialog { Title = "VisoLoom · Settings check", ClientSize = new Size(480, 620), MinimumSize = new Size(360, 400), Resizable = true };
        var narrow = new Button { Text = "360 pt" };
        var check = new Button { Text = "Check layout" };
        var done = new Button { Text = "Done" };
        var layout = new DynamicLayout { Padding = 8, Spacing = new Size(6, 6) };
        layout.AddRow(SettingsLayout.Actions(narrow, check, done));
        layout.Add(settings, xscale: true, yscale: true);
        dialog.Content = layout;
        dialog.AbortButton = done;
        narrow.Click += (_, _) => dialog.ClientSize = new Size(360, 620);
        done.Click += (_, _) => dialog.Close();
        var states = new List<object>();
        var checks = new Dictionary<string, bool>();
        string? error = null;
        long revision = ConnectionSettings.Revision;
        dialog.Shown += (_, _) => Record("opened");
        dialog.SizeChanged += (_, _) => Record("resized");
        check.Click += (_, _) =>
        {
            try { CheckLayout(); }
            catch (Exception ex)
            {
                error = ex.ToString();
                check.Text = "Check failed";
            }
        };

        void CheckLayout()
        {
            void Verify(string name, bool passed) => checks[name] = checks.GetValueOrDefault(name, true) && passed;

            var openAi = ById<Button>("openai-connection");
            var openAiDetails = ById<Panel>("openai-details");
            var openAiKey = ById<PasswordBox>("openai-key");
            var googleDetails = ById<Panel>("google-details");
            var googleKey = ById<PasswordBox>("google-key");

            foreach (var row in Descendants(settings).OfType<Panel>().Where(panel => panel.ID?.EndsWith("-details") == true && panel.Visible))
                ById<Button>(row.ID![..^"-details".Length] + "-connection").PerformClick();
            Verify("compactSections", MainHeadings().SequenceEqual(["Local models", "Cloud providers", "Your server", "Image saving"]));
            openAi.PerformClick();
            Verify("connectedModelAndMaskedKey", openAiDetails.Visible && HasVisibleLabel("Image models") &&
                HasVisibleLabel(OpenAiImageProvider.Model.DisplayName, contains: true) && !Visible(openAiKey));
            Verify("refreshInRow", Visible(Button("Refresh models")));
            Button("Change API key…", openAiDetails).PerformClick();
            Verify("replacementField", Visible(openAiKey) && openAiKey.Text.Length == 0);
            Button("Hide API key", openAiDetails).PerformClick();
            ById<Button>("google-connection").PerformClick();
            Verify("providerRowsIndependent", googleDetails.Visible && Visible(googleKey) && !openAiDetails.Visible);
            ById<Button>("google-connection").PerformClick();

            var browseModels = Button("Browse models…");
            var useExisting = Button("Use existing files…");
            if (Visible(Button("Change…"))) browseModels.PerformClick();
            if (Visible(Button("Choose folder…", settings))) useExisting.PerformClick();
            browseModels.PerformClick();
            Verify("catalogueAndDownloadFolder", HasVisibleLabel("Download folder") && Visible(Button("Change…")));
            useExisting.PerformClick();
            Verify("existingFilesInline", Visible(Button("Choose folder…")) && !HasVisibleLabel("Download folder"));
            useExisting.PerformClick();

            var options = Button("Options…");
            if (HasVisibleLabel("Additional copy")) options.PerformClick();
            options.PerformClick();
            Verify("savingOptionsInline", HasVisibleLabel("Additional copy") && HasVisibleLabel("No additional folder"));
            options.PerformClick();
            Verify("savingOptionsCollapse", !HasVisibleLabel("Additional copy"));
            Verify("serverEmptyState", HasVisibleLabel("No server added"));
            openAi.PerformClick();
            Record("checked");
        }
        try { dialog.ShowModal(Application.Instance.Windows.FirstOrDefault(window => window.Title.Contains("Rhino", StringComparison.OrdinalIgnoreCase))); }
        finally
        {
            Record("closed");
            File.WriteAllText(report, JsonSerializer.Serialize(new { states, checks, error, savingChanged, connectionUnchanged = revision == ConnectionSettings.Revision }));
        }

        Button Button(string text, Control? parent = null) =>
            Descendants(parent ?? settings).OfType<Button>().First(button => button.Text == text);
        T ById<T>(string id) where T : Control => Descendants(settings).OfType<T>().First(control => control.ID == id);
        bool HasVisibleLabel(string text, bool contains = false) =>
            Descendants(settings).OfType<Label>().Any(label => Visible(label) && (contains ? label.Text.Contains(text) : label.Text == text));
        IEnumerable<string> MainHeadings() => Descendants(settings).OfType<Label>().Where(label => label.Text is
            "Local models" or "Cloud providers" or "Your server" or "Image saving").Select(label => label.Text);
        void Record(string action) => states.Add(new
        {
            action, width = dialog.ClientSize.Width,
            horizontalOverflow = Descendants(settings).OfType<Scrollable>()
                .Where(scroll => Visible(scroll)).Select(scroll => Math.Max(0, scroll.ScrollSize.Width - scroll.VisibleRect.Width)).DefaultIfEmpty().Max()
        });
    }

    internal static bool Visible(Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent)
            if (!current.Visible) return false;
        return true;
    }

    internal static IEnumerable<Control> Descendants(Control control)
    {
        yield return control;
        if (control is Container container)
            foreach (var child in container.Controls)
                foreach (var descendant in Descendants(child)) yield return descendant;
    }
}

using Eto.Forms;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal static class ImageSavingSettings
{
    public static string? OutputFolder
    {
        get
        {
            string folder = VisoLoomPlugIn.Instance!.Settings.GetString("Images.OutputFolder", "");
            return string.IsNullOrWhiteSpace(folder) ? null : folder;
        }
        private set => VisoLoomPlugIn.Instance!.Settings.SetString("Images.OutputFolder", value ?? "");
    }

    public static Control CreateControl() => CreateControl(OutputFolder, folder => OutputFolder = folder);

    internal static Control CreateControl(string? outputFolder, Action<string?> saveFolder, string? automaticFolder = null)
    {
        automaticFolder ??= RenderImageStore.DefaultFolder;
        var summary = VisoLoomPanel.CreateLabel("");
        var current = VisoLoomPanel.CreateLabel("");
        var status = VisoLoomPanel.CreateLabel("");
        status.Visible = false;
        var options = new Button { Text = "Options…" };
        var choose = new Button { Text = "Choose folder…" };
        var reset = new Button { Text = "Remove additional folder" };
        var open = new Button { Text = "Open saved images" };
        var fields = SettingsLayout.Inset(SettingsLayout.Stack(
            SettingsLayout.Row(SettingsLayout.Caption("Additional copy", current), choose),
            SettingsLayout.Actions(reset),
            SettingsLayout.Rule(),
            VisoLoomPanel.CreateLabel("Automatic folder", bold: true),
            VisoLoomPanel.CreateLabel(automaticFolder),
            SettingsLayout.Actions(open), status));
        fields.Visible = false;
        var layout = SettingsLayout.Stack(
            SettingsLayout.Row(SettingsLayout.Caption("Image saving", summary), options),
            fields);
        options.Click += (_, _) => fields.Visible = !fields.Visible;
        choose.Click += (_, _) =>
        {
            using var picker = new SelectFolderDialog { Title = "Choose a folder for future render images" };
            if (picker.ShowDialog(layout) != DialogResult.Ok) return;
            if (!Path.IsPathFullyQualified(picker.Directory) || !Directory.Exists(picker.Directory))
            { SetStatus("Choose an available folder."); return; }
            saveFolder(picker.Directory);
            outputFolder = picker.Directory;
            Refresh();
        };
        reset.Click += (_, _) =>
        {
            saveFolder(null);
            outputFolder = null;
            Refresh();
            choose.Focus();
        };
        open.Click += (_, _) =>
        {
            try
            {
                if (!Directory.Exists(automaticFolder)) { SetStatus("The folder will be created when the first image is saved."); return; }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(automaticFolder) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
            { SetStatus("Could not open the automatic folder."); }
        };
        Refresh();
        return layout;

        void Refresh()
        {
            summary.Text = outputFolder is null ? "Saved automatically to VisoLoom’s renders folder" : "Saved automatically · Additional copy enabled";
            current.Text = outputFolder ?? "No additional folder";
            reset.Visible = outputFolder is not null;
            SetStatus("");
        }

        void SetStatus(string message)
        {
            status.Text = message;
            status.Visible = message.Length != 0;
        }
    }
}

using Eto.Drawing;
using Eto.Forms;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal static class ImageSavingSettings
{
    public static string? OutputFolder
    {
        get
        {
            string folder = VisoLoomPlugIn.Instance.Settings.GetString("Images.OutputFolder", "");
            return string.IsNullOrWhiteSpace(folder) ? null : folder;
        }
        private set => VisoLoomPlugIn.Instance.Settings.SetString("Images.OutputFolder", value ?? "");
    }

    public static Control CreateControl() => CreateControl(OutputFolder, folder => OutputFolder = folder);

    internal static Control CreateControl(string? outputFolder, Action<string?> saveFolder)
    {
        var current = VisoLoomPanel.CreateLabel("");
        var choose = new Button { Text = "Choose additional folder…" };
        var reset = new Button { Text = "Remove additional folder" };
        var open = new Button { Text = "Open saved images" };
        var layout = new DynamicLayout { Spacing = new Size(8, 8) };
        layout.AddRow(VisoLoomPanel.CreateLabel("Every completed image is saved automatically.", bold: true));
        layout.AddRow(VisoLoomPanel.CreateLabel(RenderImageStore.DefaultFolder));
        layout.AddRow(VisoLoomPanel.CreateLabel("Completed PNGs stay here until you remove them."));
        layout.AddRow(open);
        layout.AddRow(current);
        layout.AddRow(choose);
        layout.AddRow(reset);
        layout.AddRow(VisoLoomPanel.CreateLabel("Choose an additional folder to receive a copy of future render images."));
        layout.Add(null);
        choose.Click += (_, _) =>
        {
            using var picker = new SelectFolderDialog { Title = "Choose a folder for future render images" };
            if (picker.ShowDialog(layout) != DialogResult.Ok) return;
            if (!Path.IsPathFullyQualified(picker.Directory) || !Directory.Exists(picker.Directory))
            { current.Text = "Choose an available folder."; return; }
            saveFolder(picker.Directory);
            outputFolder = picker.Directory;
            Refresh();
        };
        reset.Click += (_, _) => { saveFolder(null); outputFolder = null; Refresh(); };
        open.Click += (_, _) =>
        {
            try
            {
                if (!Directory.Exists(RenderImageStore.DefaultFolder)) { current.Text = "The automatic folder will be created when the first image is saved."; return; }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RenderImageStore.DefaultFolder) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
            { current.Text = "Could not open the folder. Its path is shown above."; }
        };
        Refresh();
        return layout;
        void Refresh()
        {
            current.Text = outputFolder is { } folder ? "Additional folder\n" + folder : "Additional folder · none";
            reset.Enabled = outputFolder is not null;
        }
    }
}

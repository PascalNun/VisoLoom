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

    public static void Show(Control owner)
    {
        using var dialog = new Dialog { Title = "VisoLoom · Image saving", ClientSize = new Size(460, 420), Resizable = true };
        var current = VisoLoomPanel.CreateLabel("");
        var choose = new Button { Text = "Choose output folder…" };
        var reset = new Button { Text = "Use automatic folder only" };
        var open = new Button { Text = "Open automatic image folder" };
        var close = new Button { Text = "Done" };
        var layout = new DynamicLayout { Spacing = new Size(8, 8) };
        layout.AddRow(VisoLoomPanel.CreateLabel("Every completed image is saved automatically.", bold: true));
        layout.AddRow(VisoLoomPanel.CreateLabel(RenderImageStore.DefaultFolder));
        layout.AddRow(VisoLoomPanel.CreateLabel("These PNGs are kept separately from temporary scene history. No automatic image cleanup is applied."));
        layout.AddRow(open);
        layout.AddRow(current);
        layout.AddRow(choose);
        layout.AddRow(reset);
        layout.AddRow(VisoLoomPanel.CreateLabel("An optional output folder receives an extra PNG for future renders. Changing it leaves running jobs and existing images in their original folders. Scene files and prompts stay in private history."));
        var outer = new DynamicLayout { Padding = 12, Spacing = new Size(8, 8) };
        outer.Add(new Scrollable { Content = layout, Border = BorderType.None, ExpandContentWidth = true }, yscale: true);
        outer.AddRow(close);
        dialog.Content = outer;
        dialog.AbortButton = close;
        close.Click += (_, _) => dialog.Close();
        choose.Click += (_, _) =>
        {
            using var picker = new SelectFolderDialog { Title = "Choose a folder for future render images" };
            if (picker.ShowDialog(dialog) != DialogResult.Ok) return;
            if (!Path.IsPathFullyQualified(picker.Directory) || !Directory.Exists(picker.Directory))
            { current.Text = "Choose an available folder."; return; }
            OutputFolder = picker.Directory;
            Refresh();
        };
        reset.Click += (_, _) => { OutputFolder = null; Refresh(); };
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
        dialog.ShowModal(owner);
        void Refresh() { current.Text = OutputFolder is { } folder ? "Additional output folder:\n" + folder : "Additional output folder: none"; reset.Enabled = OutputFolder is not null; }
    }
}

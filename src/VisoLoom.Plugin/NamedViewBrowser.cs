using Eto.Drawing;
using Eto.Forms;
using Rhino;

namespace VisoLoom.Plugin;

internal static class NamedViewBrowser
{
    public static string? Choose(Control owner, RhinoDoc doc, string? selected)
    {
        using var dialog = new Dialog { Title = "VisoLoom · Named views", ClientSize = new Size(500, 650), Resizable = true };
        var list = new ListBox { Width = 1, Height = 150 };
        foreach (var view in doc.NamedViews)
            list.Items.Add(new ListItem { Key = view.Name, Text = view.Name });
        var image = new ImageView { Width = 1, Height = 220 };
        var description = VisoLoomPanel.CreateLabel("Choose a saved composition to preview it here. Your Rhino camera stays in place.");
        var refresh = new Button { Text = "Refresh thumbnail", Enabled = false };
        var choose = new Button { Text = "Choose view", Enabled = false };
        var cancel = new Button { Text = "Cancel" };
        Bitmap? bitmap = null;
        string? choice = null;
        var layout = new DynamicLayout { Padding = 12, Spacing = new Size(8, 8) };
        layout.AddRow(VisoLoomPanel.CreateLabel("Named views", bold: true));
        layout.AddRow(list);
        layout.Add(image, xscale: true, yscale: true);
        layout.AddRow(description);
        layout.AddRow(refresh);
        layout.AddRow(VisoLoomPanel.CreateLabel("Thumbnail only · current Rhino display style. Show the view in Rhino, then Preview view to review the exact image for rendering."));
        layout.AddRow(new TableLayout { Spacing = new Size(8, 8), Rows = { new TableRow(new TableCell(choose, scaleWidth: true), cancel) } });
        dialog.Content = layout;
        dialog.DefaultButton = choose;
        dialog.AbortButton = cancel;
        void Preview()
        {
            image.Image = null;
            bitmap?.Dispose();
            bitmap = null;
            string? name = list.SelectedKey;
            choose.Enabled = refresh.Enabled = name is not null && doc.NamedViews.FindByName(name) >= 0;
            if (!choose.Enabled)
            {
                description.Text = "This named view is unavailable. Choose another saved view.";
                return;
            }
            try
            {
                byte[] png = NamedViewThumbnail.Create(doc, name!);
                using var stream = new MemoryStream(png);
                bitmap = new Bitmap(stream);
                image.Image = bitmap;
                description.Text = name + " · composition thumbnail";
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
            {
                description.Text = "Thumbnail unavailable. You can still choose this view and show it in Rhino.";
            }
        }
        list.SelectedIndexChanged += (_, _) => Preview();
        refresh.Click += (_, _) => Preview();
        choose.Click += (_, _) =>
        {
            if (list.SelectedKey is { } name && doc.NamedViews.FindByName(name) >= 0)
            { choice = name; dialog.Close(); }
            else Preview();
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Load += (_, _) =>
        {
            if (selected is not null && list.Items.Any(i => i.Key == selected)) list.SelectedKey = selected;
            else if (list.Items.Count > 0) list.SelectedIndex = 0;
            else description.Text = "No named views yet. Save a composition in Rhino’s Named Views panel.";
        };
        try { dialog.ShowModal(owner); }
        finally { image.Image = null; bitmap?.Dispose(); }
        return choice;
    }
}

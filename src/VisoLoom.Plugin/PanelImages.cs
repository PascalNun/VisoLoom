using Eto.Drawing;
using Eto.Forms;
using VisoLoom.Core;
using VisoLoom.Protocol;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal sealed record PanelInput(Guid Id, byte[] Png, PixelSize Size, string Description);

// One bounded preview surface for the reviewed input and this document's recent results.
internal sealed class PanelImages : Panel
{
    private const string InputKey = "input";
    private readonly DropDown picker = new() { Width = 1 };
    private readonly StackLayout thumbnails = new() { Orientation = Orientation.Horizontal, Spacing = 5 };
    private readonly ToggleButton[] tiles = Enumerable.Range(0, 3)
        .Select(_ => new ToggleButton { Width = 70, Height = 50, MinimumSize = Size.Empty }).ToArray();
    private readonly Button inputButton = new() { Text = "Prepared input", ToolTip = "Show the image prepared for the next render" };
    private readonly WrappingLabel pickerLabel = VisoLoomPanel.CreateLabel("Recent renders");
    private readonly ImageView image = new() { Width = 1, Height = 140 };
    private readonly WrappingLabel caption = VisoLoomPanel.CreateLabel("", bold: true);
    private readonly WrappingLabel note = VisoLoomPanel.CreateLabel("");
    private readonly Button open = new() { Text = "Open…", ToolTip = "Inspect the full-resolution image" };
    private readonly Button compare = new() { Text = "Compare…", ToolTip = "Compare with the original Rhino capture" };
    private readonly Button save = new() { Text = "Save PNG…" };
    private readonly StackLayout actions;
    private readonly Dictionary<Guid, Bitmap?> thumbnailImages = [];
    private Guid[] thumbnailIds = [];
    private int panelWidth;
    private PanelInput? input;
    private IReadOnlyList<CompletedRender> recent = [];
    private Guid? latestResult;
    private string? displayed;
    private Guid? failedInput;
    private Bitmap? bitmap;
    private bool updating;

    public PanelImages()
    {
        var layout = new DynamicLayout { Spacing = new Size(6, 5), Padding = new Padding(0, 5, 0, 0) };
        layout.AddRow(caption);
        layout.AddRow(image);
        var inspection = new TableLayout
        {
            Spacing = new Size(4, 0),
            Rows = { new TableRow(new TableCell(open, true), new TableCell(compare, true)) }
        };
        actions = new StackLayout
        {
            Orientation = Orientation.Vertical, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center, Spacing = 4,
            Items = { new StackLayoutItem(inspection, true), save }
        };
        layout.AddRow(actions);
        layout.AddRow(note);
        layout.AddRow(new TableLayout
        {
            Spacing = new Size(4, 0), Rows = { new TableRow(new TableCell(pickerLabel, true), inputButton) }
        });
        foreach (var tile in tiles)
            thumbnails.Items.Add(tile);
        layout.AddRow(thumbnails);
        layout.AddRow(picker);
        Content = layout;
        Visible = false;
        picker.SelectedIndexChanged += (_, _) => { if (!updating) ShowSelected(); };
        for (int i = 0; i < tiles.Length; i++)
        {
            int index = i;
            tiles[i].Click += (_, _) =>
            {
                if (index >= thumbnailIds.Length) return;
                picker.SelectedKey = thumbnailIds[index].ToString("N");
                UpdateNavigation();
            };
        }
        inputButton.Click += (_, _) => SelectInput();
        SizeChanged += (_, _) => FitPreview();
        open.Click += (_, _) => Open();
        compare.Click += (_, _) => Compare();
        save.Click += (_, _) => Save();
    }

    public DropDown Picker => picker;
    public bool IsShowingInput => picker.SelectedKey == InputKey && bitmap is not null;
    public bool InputFailed => input is not null && input.Id == failedInput;
    public void SelectInput() => picker.SelectedKey = InputKey;
    public void SetPanelWidth(int width)
    {
        panelWidth = width;
        var orientation = width < 300 ? Orientation.Vertical : Orientation.Horizontal;
        if (actions.Orientation != orientation) actions.Orientation = orientation;
        UpdateNavigation();
    }

    public void Update(PanelInput? prepared, IReadOnlyList<CompletedRender> renders)
    {
        bool newInput = prepared is not null && prepared.Id != input?.Id;
        var newest = renders.FirstOrDefault()?.Output.Metadata.ResultId;
        bool newResult = newest != latestResult;
        input = prepared;
        recent = renders;
        latestResult = newest;
        var choices = new List<ListItem>();
        if (prepared is not null) choices.Add(new() { Key = InputKey, Text = "Prepared input" });
        for (int i = 0; i < recent.Count; i++)
        {
            var meta = recent[i].Output.Metadata;
            string kind = meta.IsUnchangedReference ? "Capture" : "Render";
            string label = i == 0 ? "Latest " + kind.ToLowerInvariant() : $"{kind} {i + 1}";
            choices.Add(new() { Key = meta.ResultId.ToString("N"), Text = $"{label} · {meta.CompletedAt.ToLocalTime():T}" });
        }
        string? key = newResult && newest is { } id ? id.ToString("N") : newInput ? InputKey : picker.SelectedKey;
        if (choices.All(choice => choice.Key != key)) key = newest?.ToString("N") ?? choices.FirstOrDefault()?.Key;
        updating = true;
        try
        {
            UpdateThumbnails();
            if (!picker.Items.Select(i => (i.Key, i.Text)).SequenceEqual(choices.Select(i => (i.Key, i.Text))))
            {
                picker.Items.Clear();
                foreach (var item in choices) picker.Items.Add(item);
            }
            if (picker.SelectedKey != key) picker.SelectedKey = key;
        }
        finally { updating = false; }
        Visible = choices.Count > 0;
        ShowSelected();
    }

    private void UpdateThumbnails()
    {
        var ids = recent.Select(item => item.Output.Metadata.ResultId).ToArray();
        if (thumbnailIds.SequenceEqual(ids)) return;
        foreach (var tile in tiles) tile.Image = null;
        foreach (Guid id in thumbnailImages.Keys.Except(ids).ToArray())
        {
            thumbnailImages[id]?.Dispose();
            thumbnailImages.Remove(id);
        }
        thumbnailIds = ids;
        for (int i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i];
            tile.Visible = i < recent.Count;
            if (!tile.Visible) continue;
            var render = recent[i];
            if (!thumbnailImages.TryGetValue(ids[i], out var thumb))
            {
                thumb = MakeThumbnail(render.Output);
                thumbnailImages.Add(ids[i], thumb);
            }
            string kind = render.Output.Metadata.IsUnchangedReference ? "Capture test" : "Render";
            tile.Image = thumb;
            tile.ToolTip = $"{kind} {i + 1} · {render.Capture.ViewName} · {render.Output.Metadata.CompletedAt.ToLocalTime():g}";
        }
    }

    private static Bitmap? MakeThumbnail(ProviderResult output)
    {
        try
        {
            var frame = output.Metadata.Plan.Final;
            ResolutionPlanner.ValidateSize(frame);
            if (PngHeader.Read(output.PngBytes) != frame) return null;
            using var stream = new MemoryStream(output.PngBytes, writable: false);
            using var decoded = new Bitmap(stream);
            if (decoded.Width != frame.Width || decoded.Height != frame.Height) return null;
            double scale = Math.Min(60d / frame.Width, 38d / frame.Height);
            return new Bitmap(decoded, Math.Max(1, (int)Math.Round(frame.Width * scale)),
                Math.Max(1, (int)Math.Round(frame.Height * scale)), ImageInterpolation.High);
        }
        catch (Exception) { return null; } // The list remains available if Rhino cannot make a thumbnail.
    }

    private void UpdateNavigation()
    {
        bool gallery = panelWidth >= 245 && thumbnailIds.Length > 0 && thumbnailImages.Values.All(image => image is not null);
        thumbnails.Visible = gallery;
        picker.Visible = !gallery && picker.Items.Count > 1;
        pickerLabel.Visible = gallery || picker.Visible;
        inputButton.Visible = gallery && input is not null;
        inputButton.Enabled = picker.SelectedKey != InputKey;
        if (!gallery) return;
        int index = Array.FindIndex(thumbnailIds, id => id.ToString("N") == picker.SelectedKey);
        for (int i = 0; i < tiles.Length; i++)
            if (tiles[i].Checked != (i == index)) tiles[i].Checked = i == index;
    }

    private CompletedRender? SelectedRender => recent.FirstOrDefault(item => item.Output.Metadata.ResultId.ToString("N") == picker.SelectedKey);

    private void ShowSelected()
    {
        UpdateNavigation();
        string? key = picker.SelectedKey;
        if (key == InputKey && input is not null) key += input.Id.ToString("N");
        if (displayed == key) return;
        displayed = key;
        var render = SelectedRender;
        var prepared = picker.SelectedKey == InputKey ? input : null;
        byte[]? png = prepared?.Png ?? render?.Output.PngBytes;
        PixelSize? frame = prepared?.Size ?? render?.Output.Metadata.Plan.Final;
        caption.Text = prepared is not null ? "Prepared input · " + prepared.Description : render is null ? "" :
            $"{(render.Output.Metadata.IsUnchangedReference ? "Capture test" : "Result")} · {render.Capture.ViewName}\n{frame!.Value.Width:N0} × {frame.Value.Height:N0} px";
        string? path = render?.Saving.SavedPath;
        SetNote(render is null ? "" : path is null ? "No automatic copy. Use Save PNG… to keep this image." : "Saved automatically.", path);
        image.Image = null;
        bitmap?.Dispose();
        bitmap = null;
        if (png is not null && frame is { } size)
        {
            try
            {
                using var stream = new MemoryStream(png, writable: false);
                var decoded = new Bitmap(stream);
                if (decoded.Width != size.Width || decoded.Height != size.Height)
                {
                    decoded.Dispose();
                    throw new ArgumentException("Unexpected image dimensions.");
                }
                bitmap = decoded;
                image.Image = bitmap;
                FitPreview();
            }
            catch (Exception)
            {
                if (prepared is not null) failedInput = prepared.Id;
                SetNote("Rhino could not display this preview.");
            }
        }
        image.Visible = open.Visible = bitmap is not null;
        compare.Visible = render is not null && bitmap is not null;
        save.Visible = render is not null;
    }

    private void FitPreview()
    {
        if (bitmap is not { Width: > 0, Height: > 0 } || Size.Width < 30) return;
        int height = (int)Math.Clamp((double)Size.Width * bitmap.Height / bitmap.Width, 65, 220);
        if (image.Height != height) image.Height = height;
    }

    private void SetNote(string message, string? path = null)
    {
        note.Text = message;
        note.Visible = message.Length != 0;
        note.ToolTip = path;
    }

    private void Open()
    {
        try
        {
            if (picker.SelectedKey == InputKey && input is { } prepared)
                ImageInspectionWindow.ShowPreview(this, prepared.Png, prepared.Description);
            else if (SelectedRender is { } item)
                ImageInspectionWindow.ShowResult(this, item.Output.PngBytes, item.Output.Metadata.Plan.Final, caption.Text);
        }
        catch (Exception) { SetNote("Rhino could not open this image for inspection."); }
    }

    private void Compare()
    {
        if (SelectedRender is not { } item) return;
        try { ImageInspectionWindow.ShowComparison(this, ComparisonSnapshot.Create(item.Capture.Manifest, item.Capture.Png, item.Output)); }
        catch (Exception) { SetNote("Rhino could not open this capture and result for comparison."); }
    }

    private void Save()
    {
        if (SelectedRender is not { } item) return;
        using var dialog = new SaveFileDialog { Title = "Save VisoLoom image", FileName = item.Output.Metadata.IsUnchangedReference ? "VisoLoom-reference.png" : "VisoLoom-render.png" };
        dialog.Filters.Add(new FileFilter("PNG image", ".png"));
        if (dialog.ShowDialog(this) != DialogResult.Ok) return;
        try
        {
            string target = Path.GetFullPath(dialog.FileName);
            string temporary = Path.Combine(Path.GetDirectoryName(target)!, ".visoloom-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(item.Output.PngBytes);
                    file.Flush(true);
                }
                File.Move(temporary, target, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            SetNote("PNG exported.", target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { SetNote("The image could not be saved. Check the destination and free disk space."); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            image.Image = null;
            bitmap?.Dispose();
            foreach (var tile in tiles) tile.Image = null;
            foreach (var thumb in thumbnailImages.Values) thumb?.Dispose();
            thumbnailImages.Clear();
        }
        base.Dispose(disposing);
    }
}

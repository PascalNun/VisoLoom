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
    private enum ImageMode { Result, Source, Compare }
    private string? selectedKey;
    private string? lastResultKey;
    private ImageMode mode;
    private readonly StackLayout thumbnails = new() { Orientation = Orientation.Horizontal, Spacing = 5 };
    private readonly ToggleButton[] tiles = Enumerable.Range(0, 3)
        .Select(_ => new ToggleButton { Width = 70, Height = 50, MinimumSize = Size.Empty }).ToArray();
    private readonly Button inputButton = new() { Text = "Prepared input", ToolTip = "Show the exact image prepared for the next render" };
    private readonly ToggleButton source = new() { Text = "Source", ToolTip = "Show the Rhino image that belongs to the selected render" };
    private readonly ToggleButton result = new() { Text = "Result", ToolTip = "Show the generated image" };
    private readonly Button allRenders = new() { Text = "All project renders…" };
    private readonly StackLayout recentHeading;
    private readonly WrappingLabel pickerLabel = VisoLoomPanel.CreateLabel("Recent renders");
    private readonly ImageView image = new() { Width = 1, Height = 140 };
    private readonly WrappingLabel caption = VisoLoomPanel.CreateLabel("", bold: true);
    private readonly WrappingLabel note = VisoLoomPanel.CreateLabel("");
    private readonly Button open = new() { Text = "Open…", ToolTip = "Inspect the full-resolution image" };
    private readonly ToggleButton compare = new() { Text = "Compare", ToolTip = "Compare with the original Rhino capture" };
    private readonly Drawable wipe = new() { Width = 1, Height = 140, BackgroundColor = SystemColors.ControlBackground, Visible = false };
    private readonly Slider divider = new() { MinValue = 0, MaxValue = 100, Value = 50, Width = 1, Visible = false,
        ToolTip = "Move the divider between source and result" };
    private readonly Button save = new() { Text = "Save PNG…" };
    private readonly StackLayout actions;
    private readonly TableLayout dividerRow;
    private readonly Dictionary<Guid, Bitmap?> thumbnailImages = [];
    private Guid[] thumbnailIds = [];
    private int panelWidth;
    private PanelInput? input;
    private IReadOnlyList<CompletedRender> recent = [];
    private List<ProjectRenderRow> fileRows = [];
    private Guid? latestResult;
    private string? displayed;
    private Guid? failedInput;
    private Bitmap? bitmap;
    private Bitmap? comparisonSource;

    public PanelImages(Button? setupToggle = null)
    {
        var layout = new DynamicLayout { Spacing = new Size(6, 5), Padding = new Padding(0, 5, 0, 0) };
        var modes = SettingsLayout.Actions(source, result);
        var inspection = SettingsLayout.Actions(compare, open);
        actions = SettingsLayout.Stack(modes, inspection);
        actions.VerticalContentAlignment = VerticalAlignment.Center;
        if (setupToggle is not null)
            actions.Items.Add(setupToggle);
        layout.AddRow(actions);
        layout.AddRow(image);
        layout.AddRow(wipe);
        dividerRow = new TableLayout
        {
            Visible = false, Spacing = new Size(6, 0),
            Rows = { new TableRow(new Label { Text = "Source" }, new TableCell(divider, true), new Label { Text = "Result" }) }
        };
        layout.AddRow(dividerRow);
        layout.AddRow(caption);
        layout.AddRow(save);
        layout.AddRow(note);
        recentHeading = SettingsLayout.Row(pickerLabel, allRenders);
        layout.AddRow(recentHeading);
        foreach (var tile in tiles)
            thumbnails.Items.Add(tile);
        layout.AddRow(thumbnails);
        layout.AddRow(inputButton);
        Content = layout;
        Visible = false;
        for (int i = 0; i < tiles.Length; i++)
        {
            int index = i;
            tiles[i].Click += (_, _) =>
            {
                if (index >= thumbnailIds.Length) return;
                SelectResult(thumbnailIds[index].ToString("N"));
            };
        }
        inputButton.Click += (_, _) => SelectInput();
        allRenders.Click += (_, _) => BrowseProjectRenders();
        SizeChanged += (_, _) => FitPreview();
        open.Click += (_, _) => Open();
        source.Click += (_, _) => ShowSource();
        result.Click += (_, _) => ShowResult();
        compare.Click += (_, _) => SetMode(mode == ImageMode.Compare ? ImageMode.Result : ImageMode.Compare);
        divider.ValueChanged += (_, _) => wipe.Invalidate();
        wipe.Paint += (_, e) => PaintComparison(e.Graphics);
        save.Click += (_, _) => Save();
    }

    public event EventHandler? SelectionChanged;
    public int ChoiceCount => thumbnailIds.Length + (input is null ? 0 : 1);
    public bool IsShowingInput => selectedKey == InputKey && bitmap is not null;
    public bool InputFailed => input is not null && input.Id == failedInput;
    public void SelectInput()
    {
        if (input is not null)
            SetSelection(InputKey, notify: true);
    }

    public void SetPanelWidth(int width)
    {
        panelWidth = width;
        var orientation = width < 400 ? Orientation.Vertical : Orientation.Horizontal;
        if (actions.Orientation != orientation) actions.Orientation = orientation;
        var headingOrientation = width < 300 ? Orientation.Vertical : Orientation.Horizontal;
        if (recentHeading.Orientation != headingOrientation) recentHeading.Orientation = headingOrientation;
        UpdateNavigation();
    }

    public void Update(PanelInput? prepared, IReadOnlyList<CompletedRender> renders, IReadOnlyList<ProjectRenderRow> projectRenders)
    {
        bool newInput = prepared is not null && prepared.Id != input?.Id;
        var newest = renders.FirstOrDefault()?.Output.Metadata.ResultId;
        bool newResult = newest != latestResult;
        input = prepared;
        recent = renders;
        var inMemory = renders.Select(item => item.Output.Metadata.ResultId).ToHashSet();
        fileRows = projectRenders.Where(row => !inMemory.Contains(row.ResultId)).ToList();
        latestResult = newest;
        UpdateThumbnails();

        string? key = newResult && newest is { } id ? id.ToString("N") : newInput ? InputKey : selectedKey;
        if ((key != InputKey || input is null) && !HasResult(key))
            key = FirstResultKey() ?? (input is null ? null : InputKey);
        Visible = ChoiceCount > 0;
        SetSelection(key, notify: false);
    }

    private string? FirstResultKey() => thumbnailIds.Length == 0 ? null : thumbnailIds[0].ToString("N");

    private bool HasResult(string? key) => Guid.TryParseExact(key, "N", out var id) && Array.IndexOf(thumbnailIds, id) >= 0;

    private void SelectResult(string key) => SetSelection(key, notify: true);

    private void SetSelection(string? key, bool notify)
    {
        if (selectedKey != key)
        {
            selectedKey = key;
            if (HasResult(key))
                lastResultKey = key;
            mode = ImageMode.Result;
        }
        if (notify)
            mode = ImageMode.Result;
        ShowSelected();
        if (notify)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowResult()
    {
        if (selectedKey != InputKey)
            SetMode(ImageMode.Result);
        else if ((HasResult(lastResultKey) ? lastResultKey : FirstResultKey()) is { } key)
            SelectResult(key);
    }

    private void ShowSource()
    {
        if (SelectedRender is not null)
            SetMode(ImageMode.Source);
        else if (selectedKey == InputKey)
            UpdateImageMode();
        else if (!HasResult(selectedKey))
            SelectInput();
    }

    private void SetMode(ImageMode next)
    {
        if (next != ImageMode.Result && !LoadComparisonSource())
        {
            UpdateImageMode();
            return;
        }
        mode = next;
        UpdateImageMode();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateThumbnails()
    {
        var ids = recent.Select(item => item.Output.Metadata.ResultId)
            .Concat(fileRows.Select(row => row.ResultId)).ToArray();
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
            tile.Visible = i < ids.Length;
            if (!tile.Visible) continue;
            if (!thumbnailImages.TryGetValue(ids[i], out var thumb))
            {
                thumb = i < recent.Count ? MakeThumbnail(recent[i].Output) : MakeThumbnail(fileRows[i - recent.Count]);
                thumbnailImages.Add(ids[i], thumb);
            }
            string kind = i < recent.Count
                ? (recent[i].Output.Metadata.IsUnchangedReference ? "Capture test" : "Render")
                : (fileRows[i - recent.Count].IsCaptureTest ? "Capture test" : "Render");
            string toolTip = i < recent.Count
                ? $"{kind} {i + 1} · {recent[i].Capture.ViewDescription} · {recent[i].Output.Metadata.CompletedAt.ToLocalTime():g}"
                : $"{kind} {i + 1} · {fileRows[i - recent.Count].ViewName} · {fileRows[i - recent.Count].CompletedAt.ToLocalTime():g}";
            tile.Image = thumb;
            tile.ToolTip = toolTip;
        }
    }

    // A project row's thumbnail comes from its own file on disk; a missing or
    // unreadable file leaves the row listed with an honest note instead.
    private static Bitmap? MakeThumbnail(ProjectRenderRow row)
    {
        try
        {
            using var decoded = new Bitmap(row.File);
            double scale = Math.Min(60d / decoded.Width, 38d / decoded.Height);
            return new Bitmap(decoded, Math.Max(1, (int)Math.Round(decoded.Width * scale)),
                Math.Max(1, (int)Math.Round(decoded.Height * scale)), ImageInterpolation.High);
        }
        catch (Exception) { return null; }
    }

    private static Bitmap? MakeThumbnail(ProviderResult output)
    {
        try
        {
            var frame = output.Metadata.Plan.Final;
            ResolutionPlanner.ValidateSize(frame, ResolutionPlanner.ExportPixelLimit);
            if (PngHeader.ReadExport(output.PngBytes) != frame) return null;
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
        recentHeading.Visible = thumbnailIds.Length > 0;
        inputButton.Visible = input is not null && selectedKey != InputKey && thumbnailIds.Length > 0;
        if (!gallery) return;
        int index = Array.FindIndex(thumbnailIds, id => id.ToString("N") == selectedKey);
        for (int i = 0; i < tiles.Length; i++)
            if (tiles[i].Checked != (i == index)) tiles[i].Checked = i == index;
    }

    private void BrowseProjectRenders()
    {
        using var dialog = new Dialog
        {
            Title = "VisoLoom · Project renders",
            ClientSize = new Size(460, 350),
            MinimumSize = new Size(295, 230),
            Resizable = true
        };
        var list = new ListBox { Width = 1 };
        var entries = recent.Select(render =>
        {
            var result = render.Output.Metadata;
            return (result.ResultId, result.CompletedAt, render.Capture.ViewName, IsCaptureTest: result.IsUnchangedReference);
        }).Concat(fileRows.Select(row => (row.ResultId, row.CompletedAt, row.ViewName, row.IsCaptureTest)))
            .OrderByDescending(entry => entry.CompletedAt).ToArray();
        if (entries.Length == 0)
            return;

        const int pageSize = 50;
        int pageCount = 1 + (entries.Length - 1) / pageSize;
        int page = Math.Max(0, Array.FindIndex(entries, entry => entry.ResultId.ToString("N") == selectedKey)) / pageSize;
        var previous = new Button { Text = "Previous" };
        var next = new Button { Text = "Next" };
        var pageLabel = VisoLoomPanel.CreateLabel("");
        var show = new Button { Text = "Show render" };
        var cancel = new Button { Text = "Cancel" };
        var content = new DynamicLayout { Padding = 12, Spacing = new Size(6, 6) };
        if (pageCount > 1)
            content.AddRow(new TableLayout { Spacing = new Size(6, 0), Rows = { new TableRow(previous, new TableCell(pageLabel, true), next) } });
        content.Add(list, xscale: true, yscale: true);
        content.AddRow(new TableLayout { Spacing = new Size(6, 0), Rows = { new TableRow(null, cancel, show) } });
        dialog.Content = content;
        dialog.DefaultButton = show;
        dialog.AbortButton = cancel;
        string? selected = null;
        dialog.Load += (_, _) => LoadPage();
        previous.Click += (_, _) => { page--; LoadPage(); };
        next.Click += (_, _) => { page++; LoadPage(); };
        show.Click += (_, _) => { selected = list.SelectedKey; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.ShowModal(this);
        if (HasResult(selected))
            SelectResult(selected!);

        void LoadPage()
        {
            list.Items.Clear();
            foreach (var entry in entries.Skip(page * pageSize).Take(pageSize))
            {
                string view = string.IsNullOrWhiteSpace(entry.ViewName) ? "Rhino view" : entry.ViewName;
                string kind = entry.IsCaptureTest ? "Capture test · " : "";
                list.Items.Add(new ListItem { Key = entry.ResultId.ToString("N"), Text = $"{kind}{view} · {entry.CompletedAt.ToLocalTime():g}" });
            }
            if (list.Items.Any(item => item.Key == selectedKey))
                list.SelectedKey = selectedKey;
            else
                list.SelectedIndex = 0;
            previous.Enabled = page > 0;
            next.Enabled = page + 1 < pageCount;
            pageLabel.Text = $"Page {page + 1} of {pageCount}";
        }
    }

    private CompletedRender? SelectedRender => recent.FirstOrDefault(item => item.Output.Metadata.ResultId.ToString("N") == selectedKey);

    private ProjectRenderRow? SelectedFileRow =>
        SelectedRender is null ? fileRows.FirstOrDefault(row => row.ResultId.ToString("N") == selectedKey) : null;

    private void ShowSelected()
    {
        UpdateNavigation();
        string? key = selectedKey;
        if (key == InputKey && input is not null) key += input.Id.ToString("N");
        if (displayed == key)
        {
            UpdateImageMode();
            return;
        }
        displayed = key;
        var render = SelectedRender;
        var fileRow = SelectedFileRow;
        var prepared = selectedKey == InputKey ? input : null;
        // A project row keeps its pixels on disk: they are read when selected,
        // never carried in the Rhino file.
        byte[]? png = prepared?.Png ?? render?.Output.PngBytes ?? ReadProjectFile(fileRow);
        PixelSize? frame = prepared?.Size ?? render?.Output.Metadata.Plan.Final ?? fileRow?.Frame;
        if (prepared is not null)
            caption.Text = "Source image · " + prepared.Description;
        else if (render is not null)
            caption.Text = $"{(render.Output.Metadata.IsUnchangedReference ? "Capture test" : "Result")} · {render.Capture.ViewName}\n{render.Capture.DisplayStyle}\n{frame!.Value.Width:N0} × {frame.Value.Height:N0} px";
        else if (fileRow is { } row)
            caption.Text = $"{(row.IsCaptureTest ? "Capture test" : "Result")} · {row.ViewName}\n{row.DisplayStyle}\n{frame!.Value.Width:N0} × {frame.Value.Height:N0} px\n{row.Model}";
        else
            caption.Text = "";
        string? path = render?.Saving.SavedPath ?? fileRow?.File;
        // A file row names its file's own state; a missing file is information,
        // not a broken row.
        string summary = render is not null ? render.Saving.Summary : fileRow is { } row2
            ? (png is not null ? "Saved as a file." : File.Exists(row2.File)
                ? "The saved file could not be read." : "The file could not be found. It may have been moved or deleted.")
            : "";
        SetNote(summary, path);
        image.Image = null;
        bitmap?.Dispose();
        bitmap = null;
        comparisonSource?.Dispose();
        comparisonSource = null;
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
                double scale = Math.Min(1, 640d / Math.Max(size.Width, size.Height));
                if (scale < 1)
                {
                    using (decoded)
                        bitmap = new Bitmap(decoded, Math.Max(1, (int)Math.Round(size.Width * scale)),
                            Math.Max(1, (int)Math.Round(size.Height * scale)), ImageInterpolation.High);
                }
                else
                    bitmap = decoded;
                FitPreview();
            }
            catch (Exception)
            {
                if (prepared is not null) failedInput = prepared.Id;
                SetNote("Rhino could not display this preview.");
            }
        }
        UpdateImageMode();
    }

    private bool LoadComparisonSource()
    {
        if (comparisonSource is not null)
            return true;
        if (SelectedRender is not { } item || bitmap is null)
            return false;
        try
        {
            var frame = item.Output.Metadata.Plan.Final;
            var png = new RhinoImageProcessor().CreateComparisonPreview(item.Capture.Png, item.Capture.Manifest.Frame, frame);
            using var stream = new MemoryStream(png, writable: false);
            comparisonSource = new Bitmap(stream);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or InvalidOperationException or
            NotSupportedException or System.Runtime.InteropServices.ExternalException)
        {
            SetNote("Rhino could not align this source image for comparison.");
            return false;
        }
    }

    private void UpdateImageMode()
    {
        bool reviewing = selectedKey == InputKey;
        bool paired = SelectedRender is not null && bitmap is not null;
        bool comparing = paired && mode == ImageMode.Compare;
        source.Checked = reviewing || mode == ImageMode.Source;
        result.Checked = !reviewing && mode == ImageMode.Result;
        compare.Checked = comparing;
        source.Enabled = reviewing || paired;
        result.Enabled = thumbnailIds.Length > 0;
        compare.Visible = paired;
        wipe.Visible = divider.Visible = dividerRow.Visible = comparing;
        Bitmap? shown = !reviewing && mode == ImageMode.Source ? comparisonSource : bitmap;
        if (image.Image != shown)
            image.Image = shown;
        image.Visible = !comparing && shown is not null;
        open.Visible = bitmap is not null;
        save.Visible = !reviewing && (SelectedRender is not null || SelectedFileRow is not null && bitmap is not null);
        if (SelectedRender is { } render)
        {
            var frame = render.Output.Metadata.Plan.Final;
            string kind = mode switch
            {
                ImageMode.Source => "Source",
                ImageMode.Compare => "Source and result",
                _ => render.Output.Metadata.IsUnchangedReference ? "Capture test" : "Result"
            };
            caption.Text = $"{kind} · {render.Capture.ViewName}\n{render.Capture.DisplayStyle}\n{frame.Width:N0} × {frame.Height:N0} px";
        }
        if (comparing)
            wipe.Invalidate();
    }

    private void PaintComparison(Graphics graphics)
    {
        if (bitmap is not { } output || comparisonSource is not { } reference)
            return;
        float scale = Math.Min((float)wipe.Width / output.Width, (float)wipe.Height / output.Height);
        float width = output.Width * scale, height = output.Height * scale;
        var frame = new RectangleF((wipe.Width - width) / 2, (wipe.Height - height) / 2, width, height);
        ImageInspectionWindow.DrawWipe(graphics, reference, output, frame, frame.Left + frame.Width * divider.Value / 100f);
    }

    // A project row's pixels live on disk alone: read on demand, bounded, and
    // a missing or oversized file is named honestly instead of guessed at.
    private static byte[]? ReadProjectFile(ProjectRenderRow? row)
    {
        if (row is null) return null;
        try
        {
            if (File.Exists(row.File) && new FileInfo(row.File).Length <= 128L * 1024 * 1024)
            {
                byte[] bytes = File.ReadAllBytes(row.File);
                if (PngHeader.ReadExport(bytes) == row.Frame)
                    return bytes;
            }
        }
        catch (Exception) { }
        return null;
    }

    private void FitPreview()
    {
        if (bitmap is not { Width: > 0, Height: > 0 } || Size.Width < 30) return;
        int height = (int)Math.Clamp((double)Size.Width * bitmap.Height / bitmap.Width, 65, 220);
        if (image.Height != height) image.Height = height;
        if (wipe.Height != height) wipe.Height = height;
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
            if (selectedKey == InputKey && input is { } prepared)
                ImageInspectionWindow.ShowPreview(this, prepared.Png, prepared.Description);
            else if (SelectedRender is { } item)
            {
                if (mode != ImageMode.Result)
                    Compare();
                else
                    ImageInspectionWindow.ShowResult(this, item.Output.PngBytes, item.Output.Metadata.Plan.Final, caption.Text);
            }
            else if (SelectedFileRow is { } row && ReadProjectFile(row) is { } bytes)
                ImageInspectionWindow.ShowResult(this, bytes, row.Frame, caption.Text);
        }
        catch (Exception) { SetNote("Rhino could not open this image for inspection."); }
    }

    private void Compare()
    {
        if (SelectedRender is not { } item) return;
        try
        {
            var snapshot = ComparisonSnapshot.Create(item.Capture.Manifest, item.Capture.Png, item.Output, new RhinoImageProcessor());
            ImageInspectionWindow.ShowComparison(this, snapshot, item.Capture.ViewDescription, startWithWipe: mode == ImageMode.Compare);
        }
        catch (Exception) { SetNote("Rhino could not open this capture and result for comparison."); }
    }

    private void Save()
    {
        byte[]? bytes;
        string fileName;
        if (SelectedRender is { } item)
        {
            bytes = item.Output.PngBytes;
            fileName = item.Output.Metadata.IsUnchangedReference ? "VisoLoom-reference.png" : "VisoLoom-render.png";
        }
        else if (SelectedFileRow is { } row)
        {
            // A project row exports a copy of its own file; the saved original
            // stays where the project recorded it.
            bytes = ReadProjectFile(row);
            fileName = Path.GetFileName(row.File);
            if (bytes is null)
            {
                SetNote("The file could not be read, so no copy can be exported.", row.File);
                return;
            }
        }
        else
            return;
        using var dialog = new SaveFileDialog { Title = "Save VisoLoom image", FileName = fileName };
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
                    file.Write(bytes);
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
            comparisonSource?.Dispose();
            foreach (var tile in tiles) tile.Image = null;
            foreach (var thumb in thumbnailImages.Values) thumb?.Dispose();
            thumbnailImages.Clear();
        }
        base.Dispose(disposing);
    }
}

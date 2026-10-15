using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using VisoLoom.Core;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

[Guid("bbf16d98-c114-4f65-9790-6ee8dfe5806e")]
public sealed class VisoLoomPanel : Panel
{
    private readonly uint serial;
    private readonly CaptureCoordinator session;
    private readonly DropDown model = new() { Width = 1 };
    private readonly DropDown preset = new() { Width = 1 };
    private readonly TextArea prompt = new() { Width = 1, Height = 70 };
    private readonly WrappingLabel presetLabel = CreateLabel("Preset");
    private readonly Expander promptSection = new() { Header = "Prompt · optional" };
    private readonly WrappingLabel captureOnly = CreateLabel("Choose an image-editing model to use a preset and prompt.");
    private readonly WrappingLabel disclosure = CreateLabel("");
    private readonly WrappingLabel resultLabel = CreateLabel("Returned reference · no generation", bold: true);
    private readonly Button reviewPrompt = new() { Text = "Review prepared prompt…", Enabled = false };
    private readonly Button preview = new() { Text = "Preview capture" };
    private readonly WrappingLabel previewStatus = CreateLabel("Preview locally before rendering. Nothing is sent.");
    private readonly Button inspectPreview = new() { Text = "Enlarge preview…", Enabled = false };
    private readonly Button compare = new() { Text = "Compare with Rhino capture…", Enabled = false };
    private readonly Button imageSaving = new() { Text = "Image saving…" };
    private readonly WrappingLabel savedImage = CreateLabel("");
    private readonly Button settings = new() { Text = "Settings…" };
    private readonly WrappingLabel viewLabel = CreateLabel("");
    private readonly DropDown sourceView = new() { Width = 1 };
    private readonly Button browseViews = new() { Text = "Browse named views…" };
    private readonly Button showNamedView = new() { Text = "Show named view in Rhino", Visible = false };
    private readonly Panel resultSection = new() { Visible = false };
    private bool updatingControls;
    private bool updatingModels;
    private bool loaded;
    private bool customHeightEdited;
    private readonly WrappingLabel status = CreateLabel("");
    private readonly WrappingLabel destination = CreateLabel("");
    private readonly DropDown resolution = new()
    {
        Width = 1,
        Items =
        {
            "Viewport",
            "2× Viewport",
            "4× Viewport",
            "Custom · aspect locked"
        },
        SelectedIndex = 0
    };
    private readonly NumericStepper width = new()
    {
        MinValue = 1,
        MaxValue = 16384,
        DecimalPlaces = 0,
        Value = 1920,
        Width = 80
    };
    private readonly NumericStepper height = new()
    {
        MinValue = 1,
        MaxValue = 16384,
        DecimalPlaces = 0,
        Value = 1080,
        Width = 80
    };
    private readonly WrappingLabel dimensions = CreateLabel("");
    private readonly Panel customSizeRow = new();
    private readonly Button test = new() { Text = "Test capture" };
    private readonly Button cancel = new()
    {
        Text = "Cancel",
        Enabled = false
    };
    private readonly Button save = new()
    {
        Text = "Save PNG…",
        Enabled = false
    };
    private readonly Button reconnect = new()
    {
        Text = "Reconnect",
        Visible = false
    };
    private readonly WrappingLabel capturedLabel = CreateLabel("Captured Rhino view", bold: true);
    // The scaled rows fill the panel; image pixel width must not set its minimum width.
    private readonly ImageView source = new() { Width = 1, Height = 170 };
    private readonly ImageView result = new() { Width = 1, Height = 170 };
    private Bitmap? sourceBitmap;
    private Bitmap? resultBitmap;
    private Guid? shownScene;
    private Guid? shownJob;
    private readonly UITimer timer = new() { Interval = 1 };
    public VisoLoomPanel(RhinoDoc doc)
    {
        serial = doc.RuntimeSerialNumber;
        session = CaptureCoordinator.ForDocument(serial);
        Padding = 12;
        var layout = new DynamicLayout
        {
            Spacing = new Size(8, 8)
        };
        layout.AddRow(new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows = { new TableRow(new TableCell(CreateLabel("VisoLoom", bold: true), scaleWidth: true), settings) }
        });
        var viewSection = Section("View");
        viewSection.AddRow(sourceView);
        viewSection.AddRow(browseViews);
        viewSection.AddRow(showNamedView);
        viewSection.AddRow(viewLabel);
        layout.AddRow(viewSection);
        var imageSection = Section("Image");
        imageSection.AddRow(CreateLabel("Model"));
        imageSection.AddRow(model);
        imageSection.AddRow(destination);
        imageSection.AddRow(presetLabel);
        foreach (var item in PromptPresets.All)
            preset.Items.Add(item.Name);
        preset.SelectedIndex = Math.Max(0, PromptPresets.All.ToList().FindIndex(p => p.Id == session.SelectedPreset.Id));
        prompt.Text = session.UserPrompt;
        imageSection.AddRow(preset);
        promptSection.Content = prompt;
        promptSection.Expanded = !string.IsNullOrWhiteSpace(prompt.Text);
        imageSection.AddRow(promptSection);
        imageSection.AddRow(captureOnly);
        layout.AddRow(imageSection);
        var outputSection = Section("Output size");
        outputSection.AddRow(resolution);
        // Multi-control rows need their own grid, rather than widening every panel row.
        customSizeRow.Content = new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows =
            {
                new TableRow(width, new Label { Text = "×" }, height, new TableCell(null, scaleWidth: true))
            }
        };
        outputSection.AddRow(customSizeRow);
        outputSection.AddRow(dimensions);
        layout.AddRow(outputSection);
        var previewSection = Section("Preview and render");
        previewSection.AddRow(preview);
        previewSection.AddRow(previewStatus);
        previewSection.AddRow(capturedLabel);
        previewSection.AddRow(source);
        previewSection.AddRow(inspectPreview);
        previewSection.AddRow(reviewPrompt);
        previewSection.AddRow(disclosure);
        previewSection.AddRow(new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows =
            {
                new TableRow(new TableCell(test, scaleWidth: true), cancel)
            }
        });
        previewSection.AddRow(status);
        previewSection.AddRow(reconnect);
        layout.AddRow(previewSection);
        var resultLayout = Section("Result");
        resultLayout.AddRow(resultLabel);
        resultLayout.AddRow(result);
        resultLayout.AddRow(compare);
        resultLayout.AddRow(savedImage);
        resultLayout.AddRow(imageSaving);
        resultLayout.AddRow(save);
        resultSection.Content = resultLayout;
        layout.AddRow(resultSection);
        layout.AddRow(null);
        Content = new Scrollable
        {
            Content = layout,
            Border = BorderType.None,
            ExpandContentWidth = true
        };
        if (session.CustomSize is { } customSize)
        {
            width.Value = customSize.Width;
            height.Value = customSize.Height;
            resolution.SelectedIndex = 3;
        }
        else
            resolution.SelectedIndex = session.Multiplier switch { 2 => 1, 4 => 2, _ => 0 };
        resolution.SelectedIndexChanged += (_, _) =>
        {
            if (updatingControls)
                return;
            session.InvalidatePreview();
            if (resolution.SelectedIndex == 3 && session.CustomSize is null)
            {
                var document = RhinoDoc.FromRuntimeSerialNumber(serial);
                try
                {
                    if (document is not null)
                        SetCustomSize(SceneBuilder.ViewportSize(session.SourceView(document)));
                }
                catch (InvalidOperationException) { /* RefreshView explains unavailable sources. */ }
            }
            RefreshView();
        };
        sourceView.SelectedIndexChanged += (_, _) =>
        {
            if (updatingControls || sourceView.SelectedKey is null)
                return;
            string key = sourceView.SelectedKey;
            session.InvalidatePreview();
            session.NamedView.Select(key.StartsWith("named:", StringComparison.Ordinal) ? key[6..] : null);
            session.SourceViewportId = key is "active" || session.NamedView.Name is not null ? null : Guid.Parse(key);
            RefreshView();
        };
        browseViews.Click += (_, _) =>
        {
            if (RhinoDoc.FromRuntimeSerialNumber(serial) is { } document &&
                NamedViewBrowser.Choose(this, document, session.NamedView.Name) is { } name)
            {
                session.InvalidatePreview();
                session.NamedView.Select(name);
                session.SourceViewportId = null;
                updatingControls = true;
                try { sourceView.SelectedKey = "named:" + name; }
                finally { updatingControls = false; }
                RefreshView();
            }
        };
        showNamedView.Click += (_, _) =>
        {
            if (RhinoDoc.FromRuntimeSerialNumber(serial) is { } document)
                session.ShowNamedView(document);
        };
        width.ValueChanged += (_, _) =>
        {
            if (updatingControls)
                return;
            session.InvalidatePreview();
            customHeightEdited = false;
            RefreshView();
        };
        height.ValueChanged += (_, _) =>
        {
            if (updatingControls)
                return;
            session.InvalidatePreview();
            customHeightEdited = true;
            RefreshView();
        };
        settings.Click += (_, _) => ConnectionSettingsControl.ShowSettings(this, Configure);
        preset.SelectedIndexChanged += (_, _) =>
        {
            session.InvalidatePreview();
            session.SelectedPreset = PromptPresets.All[preset.SelectedIndex];
            RefreshView();
        };
        prompt.TextChanged += (_, _) =>
        {
            session.InvalidatePreview();
            session.UserPrompt = prompt.Text;
            RefreshView();
        };
        preview.Click += (_, _) =>
        {
            RefreshView();
            if (RhinoDoc.FromRuntimeSerialNumber(serial) is { } document)
                session.Preview(document);
        };
        inspectPreview.Click += (_, _) =>
        {
            if (session.Capture is not { } capture || sourceBitmap is null)
                return;
            try { ImageInspectionWindow.ShowPreview(this, session.PreviewPng ?? capture.Png, capturedLabel.Text); }
            catch (Exception) { status.Text = "The host could not open this input image for inspection. No capture or render was started."; }
        };
        compare.Click += (_, _) =>
        {
            if (session.Capture is not { } capture || session.Result is not { } output) return;
            try
            {
                var snapshot = ComparisonSnapshot.Create(capture.Manifest, capture.Png, output);
                ImageInspectionWindow.ShowComparison(this, snapshot);
            }
            catch (Exception) { status.Text = "Could not open the captured source/result pair. No new capture or render was started."; }
        };
        reviewPrompt.Click += (_, _) =>
        {
            using var dialog = new Dialog { Title = "VisoLoom · Prepared prompt", ClientSize = new Size(560, 500), Resizable = true };
            var close = new Button { Text = "Close" };
            var layout = new DynamicLayout { Padding = 8, Spacing = new Size(8, 8) };
            layout.Add(new TextArea { ReadOnly = true, Text = session.Intent?.EffectivePrompt ?? "" }, yscale: true);
            layout.AddRow(close);
            dialog.Content = layout;
            dialog.AbortButton = close;
            close.Click += (_, _) => dialog.Close();
            dialog.ShowModal(this);
        };
        model.SelectedIndexChanged += (_, _) =>
        {
            if (!updatingModels && model.SelectedIndex >= 0)
            {
                session.SelectedProvider = model.SelectedIndex == 2 ? "managed" : model.SelectedIndex == 1 ? "openai" : "local";
                Refresh();
                RefreshView();
            }
        };
        ConnectionSettings.Changed += ConnectionChanged;
        UpdateModels();
        test.Click += (_, _) =>
        {
            var document = RhinoDoc.FromRuntimeSerialNumber(serial);
            if (document is null)
                return;
            RefreshView();
            session.Start(document, PromptPresets.All[preset.SelectedIndex], prompt.Text);
        };
        reconnect.Click += (_, _) => session.Reconnect();
        cancel.Click += (_, _) => session.Cancel();
        save.Click += (_, _) => Save();
        imageSaving.Click += (_, _) => ImageSavingSettings.Show(this);
        session.Changed += SessionChanged;
        foreach (var menu in new[] { sourceView, model, preset, resolution })
        {
            // Polling a native layout while its menu is open can dismiss the menu.
            menu.DropDownOpening += (_, _) => timer.Stop();
            menu.DropDownClosed += (_, _) =>
            {
                if (loaded)
                {
                    timer.Start();
                    RefreshView();
                }
            };
        }
        timer.Elapsed += (_, _) =>
        {
            RefreshView();
            _ = session.TryConnectAutomaticallyAsync();
        };
        UnLoad += (_, _) => { loaded = false; timer.Stop(); };
        Load += (_, _) =>
        {
            loaded = true;
            timer.Start();
            Refresh();
        };
        Refresh();
    }

    private static DynamicLayout Section(string title)
    {
        var layout = new DynamicLayout { Padding = new Padding(0, 6, 0, 4), Spacing = new Size(8, 6) };
        layout.AddRow(CreateLabel(title, bold: true));
        return layout;
    }

    internal static WrappingLabel CreateLabel(string text, bool bold = false)
    {
        var label = new WrappingLabel
        {
            Text = text,
            TextAlignment = TextAlignment.Left,
            Wrap = WrapMode.Word,
            Width = 1
        };
        if (bold)
            label.Font = SystemFonts.Bold();
        return label;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            loaded = false;
            session.Changed -= SessionChanged;
            ConnectionSettings.Changed -= ConnectionChanged;
            timer.Dispose();
            sourceBitmap?.Dispose();
            resultBitmap?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ConnectionChanged(object? sender, EventArgs e)
    {
        UpdateModels();
        Refresh();
    }

    private void UpdateModels()
    {
        if (updatingModels) return;
        updatingModels = true;
        try
        {
            string selected = session.SelectedProvider;
            model.Items.Clear();
            model.Items.Add("This computer · capture test");
            model.Items.Add(OpenAiImageProvider.Model.DisplayName + (ConnectionSettings.IsReady ? "" : " · needs setup"));
            if (ConnectionSettings.LocalEngineAllowed && session.LocalModel is not null)
                model.Items.Add("FLUX.2 klein 4B · this computer");
            model.SelectedIndex = selected == "managed" && model.Items.Count == 3 ? 2 : selected == "openai" ? 1 : 0;
            session.SelectedProvider = model.SelectedIndex == 2 ? "managed" : model.SelectedIndex == 1 ? "openai" : "local";
        }
        finally { updatingModels = false; }
    }

    private void SessionChanged(object? sender, EventArgs e)
    {
        bool showLocal = ConnectionSettings.LocalEngineAllowed && session.LocalModel is not null;
        if (model.Items.Count != (showLocal ? 3 : 2)) UpdateModels();
        Refresh();
    }
    private void Refresh()
    {
        status.Text = session.Status;
        reconnect.Visible = session.CanReconnect;
        reconnect.Enabled = !session.IsBusy;
        bool generating = session.SelectedProvider != "local";
        bool localModel = session.SelectedProvider == "managed";
        destination.Text = localModel ? "FLUX.2 klein 4B · this computer" : generating ? "OpenAI · cloud" : "Capture test · this computer";
        destination.ToolTip = generating && !localModel ? "https://api.openai.com" : session.Endpoint.GetLeftPart(UriPartial.Authority);
        disclosure.Text = localModel ? "Render uses this image and a short prompt on this computer." : generating ? "Render sends this image and selected scene context to OpenAI." : "The capture test stays on this computer and returns the image unchanged.";
        test.Text = generating ? "Render" : "Test capture";
        cancel.Text = session.ActiveProvider == "openai" ? "Stop waiting" : "Cancel";
        resultLabel.Text = session.Result?.Metadata.IsUnchangedReference == false ? "Generated image · compare with source" : "Returned reference · no generation";
        test.Enabled = !session.IsBusy && !session.IsConnecting && session.IsConfigured;
        settings.Enabled = !session.IsBusy;
        cancel.Enabled = session.IsBusy;
        cancel.Visible = session.IsBusy;
        save.Enabled = session.Result is not null;
        reviewPrompt.Enabled = session.Intent is { EffectivePrompt.Length: > 0 };
        preview.Text = session.PreviewPng is null ? "Preview capture" : "Refresh preview";
        inspectPreview.Enabled = sourceBitmap is not null;
        resolution.Enabled = !session.IsBusy;
        sourceView.Enabled = !session.IsBusy;
        showNamedView.Enabled = !session.IsBusy;
        browseViews.Enabled = !session.IsBusy;
        model.Enabled = !session.IsBusy;
        preset.Enabled = prompt.Enabled = generating && !session.IsBusy;
        preset.Visible = presetLabel.Visible = promptSection.Visible = generating;
        captureOnly.Visible = !generating;
        if (session.Capture is { } captured && captured.Manifest.SceneId != shownScene)
        {
            try
            {
                using var stream = new MemoryStream(session.PreviewPng ?? captured.Png);
                var decoded = new Bitmap(stream);
                var canvas = session.PreviewPlan?.Native ?? captured.Manifest.Frame;
                if (decoded.Width != canvas.Width || decoded.Height != canvas.Height)
                {
                    decoded.Dispose();
                    throw new InvalidOperationException();
                }

                capturedLabel.Text = $"Input preview: {captured.ViewName} · {session.PreviewStyle}\n{canvas.Width:N0} × {canvas.Height:N0} px · captured {captured.Manifest.CapturedAt.ToLocalTime():t}";
                source.Image = decoded;
                sourceBitmap?.Dispose();
                sourceBitmap = decoded;
                shownScene = captured.Manifest.SceneId;
                result.Image = null;
                resultBitmap?.Dispose();
                resultBitmap = null;
                shownJob = null;
                inspectPreview.Enabled = true;
            }
            catch (Exception)
            {
                status.Text = "The host could not decode the captured PNG. The image is not displayed.";
                session.InvalidatePreview();
                inspectPreview.Enabled = false;
            }
        }

        if (session.Result is { } output && output.Metadata.ResultId != shownJob)
        {
            try
            {
                using var stream = new MemoryStream(output.PngBytes);
                var decoded = new Bitmap(stream);
                if (decoded.Width != session.Capture!.Manifest.Frame.Width || decoded.Height != session.Capture.Manifest.Frame.Height)
                {
                    decoded.Dispose();
                    throw new InvalidOperationException();
                }

                result.Image = decoded;
                resultBitmap?.Dispose();
                resultBitmap = decoded;
                shownJob = output.Metadata.ResultId;
            }
            catch (Exception)
            {
                status.Text = "The host could not decode the returned PNG. The image is not displayed.";
                save.Enabled = false;
            }
        }

        bool hasPreview = sourceBitmap is not null;
        capturedLabel.Visible = source.Visible = inspectPreview.Visible = reviewPrompt.Visible = hasPreview;
        resultSection.Visible = resultBitmap is not null;
        compare.Enabled = resultBitmap is not null && session.Capture is not null && session.Result is not null;
        savedImage.Text = session.ImageSave?.SavedPath is { } path ? "Saved automatically:\n" + path : session.Result is null ? "" : "Automatic saving needs attention. Use Save PNG… to keep this image.";

        RefreshView();
    }

    private void RefreshView()
    {
        var document = RhinoDoc.FromRuntimeSerialNumber(serial);
        bool custom = resolution.SelectedIndex == 3;
        customSizeRow.Visible = custom;
        width.Enabled = height.Enabled = custom && !session.IsBusy;
        if (document is null)
        {
            viewLabel.Text = "No model viewport";
            test.Enabled = false;
            preview.Enabled = false;
            return;
        }
        UpdateViews(document);
        showNamedView.Visible = session.NamedView.Name is not null;
        try
        {
            var view = session.SourceView(document);
            var viewport = view.ActiveViewport;
            viewLabel.Text = viewport.Name + " · " + (viewport.IsParallelProjection ? "Parallel"
                : viewport.IsTwoPointPerspectiveProjection ? "Two-point perspective" : "Perspective") + " · " + viewport.DisplayMode.LocalName;
            var pixels = new PixelSize(viewport.Size.Width, viewport.Size.Height);
            var multiplier = resolution.SelectedIndex switch
            {
                1 => 2,
                2 => 4,
                _ => 1
            };
            if (!session.IsBusy)
            {
                session.CustomSize = custom ? new((int)width.Value, (int)height.Value) : null;
                session.Multiplier = multiplier;
            }
            var size = custom
                ? customHeightEdited ? ResolutionPlanner.FitHeight(pixels, (int)height.Value) : ResolutionPlanner.FitWidth(pixels, (int)width.Value)
                : ResolutionPlanner.ScaleViewport(pixels, multiplier);
            var step = ResolutionPlanner.AspectStep(pixels);
            if (width.Increment != step.Width) width.Increment = step.Width;
            if (height.Increment != step.Height) height.Increment = step.Height;
            string hint = $"Sizes round to keep this view's proportions. Each step changes size by {step.Width:N0} × {step.Height:N0} pixels.";
            if (width.ToolTip != hint) width.ToolTip = hint;
            if (height.ToolTip != hint) height.ToolTip = hint;
            if (!session.IsBusy)
            {
                SetCustomSize(size);
                session.CustomSize = custom ? size : null;
                session.Multiplier = multiplier;
            }
            string sizeText = $"Capture / save: {size.Width:N0} × {size.Height:N0} px\nComplete view · proportions locked";
            if (custom)
                sizeText += " · sizes rounded to match";
            if (session.SelectedProvider != "local")
            {
                var limits = session.SelectedProvider == "managed" ? VisoLoom.Protocol.ManagedModel.Create("preview").Resolution : OpenAiImageProvider.Model.Resolution;
                var plan = ResolutionPlanner.Plan(size, size, limits);
                sizeText += $"\nModel canvas: {plan.Native.Width:N0} × {plan.Native.Height:N0} px";
                if (plan.HasPadding)
                    sizeText += " · padding removed on return";
                if (plan.RequiresResampling)
                    sizeText += $"\nModel content: {plan.ContentRectangle.Width:N0} × {plan.ContentRectangle.Height:N0} px · resized for saving";
                if (session.SelectedProvider == "managed" || plan.Native.Pixels > 2560L * 1440)
                    sizeText += "\nThis model resolution is experimental.";
            }
            dimensions.Text = sizeText;
            bool current = !session.IsBusy && session.PreviewIsCurrent(document);
            test.Enabled = !session.IsBusy && !session.IsConnecting && session.IsConfigured && current;
            preview.Enabled = !session.IsBusy;
            previewStatus.Text = session.PreviewSubmitted ? "Submitted capture. Refresh preview before another render."
                : session.PreviewPng is null ? "Preview locally before rendering. Nothing is sent."
                : current ? "Preview is current. Nothing has been sent."
                : session.IsBusy ? "Preparing preview…" : "Preview is out of date. Refresh it before rendering.";
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            dimensions.Text = "This size cannot preserve the complete view within the image limits. Choose a smaller size.";
            test.Enabled = false;
            preview.Enabled = false;
            session.InvalidatePreview();
            previewStatus.Text = "Choose a supported size, then preview again.";
        }
        catch (InvalidOperationException ex)
        {
            viewLabel.Text = ex.Message;
            dimensions.Text = "";
            test.Enabled = false;
            preview.Enabled = false;
            session.InvalidatePreview();
            previewStatus.Text = "Choose or show a supported view, then preview again.";
        }
    }

    private void SetCustomSize(PixelSize size)
    {
        updatingControls = true;
        try
        {
            if (width.Value != size.Width) width.Value = size.Width;
            if (height.Value != size.Height) height.Value = size.Height;
        }
        finally { updatingControls = false; }
    }

    private void UpdateViews(RhinoDoc document)
    {
        var views = SceneBuilder.ModelViews(document);
        var items = new List<ListItem> { new() { Key = "active", Text = "Active viewport" } };
        foreach (var named in document.NamedViews)
            items.Add(new() { Key = "named:" + named.Name, Text = "Named view · " + named.Name });
        foreach (var view in views)
        {
            string name = view.MainViewport.Name;
            if (views.Count(v => v.MainViewport.Name == name) > 1)
                name += $" · view {Array.IndexOf(views, view) + 1}";
            items.Add(new() { Key = view.MainViewport.Id.ToString("D"), Text = name });
        }
        string selected = session.NamedView.Name is { } namedName ? "named:" + namedName : session.SourceViewportId?.ToString("D") ?? "active";
        if (items.All(i => i.Key != selected))
            items.Add(new() { Key = selected, Text = "Unavailable view · choose another" });
        if (sourceView.Items.Select(i => (i.Key, i.Text)).SequenceEqual(items.Select(i => (i.Key, i.Text))))
            return;
        updatingControls = true;
        try
        {
            sourceView.Items.Clear();
            foreach (var item in items)
                sourceView.Items.Add(item);
            sourceView.SelectedKey = selected;
        }
        finally { updatingControls = false; }
    }

    private void Configure(Control owner)
    {
        var endpoint = new TextBox
        {
            Text = session.Endpoint.AbsoluteUri,
            Width = 300
        };
        var secret = new PasswordBox
        {
            Width = 300
        };
        var error = CreateLabel("The token is held in memory for this document session only.");
        var connect = new Button
        {
            Text = "Use local server"
        };
        var dismiss = new Button
        {
            Text = "Cancel"
        };
        using var dialog = new Dialog
        {
            Title = "VisoLoom · Manual local connection",
            Padding = 16,
            Resizable = false
        };
        var layout = new DynamicLayout
        {
            Spacing = new Size(8, 8)
        };
        layout.AddRow(CreateLabel("This computer only · unchanged reference", bold: true));
        layout.AddRow(CreateLabel("Server"));
        layout.AddRow(endpoint);
        layout.AddRow(CreateLabel("Session token"));
        layout.AddRow(secret);
        layout.AddRow(error);
        layout.AddRow(CreateLabel("The test launcher connects automatically. Manual settings take precedence for this session."));
        layout.AddRow(new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows =
            {
                new TableRow(new TableCell(connect, scaleWidth: true), dismiss)
            }
        });
        dialog.Content = layout;
        dialog.DefaultButton = connect;
        dialog.AbortButton = dismiss;
        dismiss.Click += (_, _) => dialog.Close();
        connect.Click += (_, _) =>
        {
            try
            {
                session.Configure(new Uri(endpoint.Text), secret.Text);
                secret.Text = "";
                dialog.Close();
            }
            catch (Exception ex) when (ex is ArgumentException or UriFormatException or InvalidOperationException)
            {
                error.Text = ex.Message;
            }
        };
        timer.Stop();
        try
        {
            dialog.ShowModal(owner);
        }
        finally
        {
            secret.Text = "";
            timer.Start();
        }
    }

    private void Save()
    {
        if (session.Result is not { } output || resultBitmap is null)
            return;
        using var dialog = new SaveFileDialog
        {
            Title = output.Metadata.IsUnchangedReference ? "Save unchanged reference" : "Save generated image",
            FileName = output.Metadata.IsUnchangedReference ? "VisoLoom-reference.png" : "VisoLoom-render.png"
        };
        dialog.Filters.Add(new FileFilter("PNG image", ".png"));
        if (dialog.ShowDialog(this) != DialogResult.Ok)
            return;
        try
        {
            string target = Path.GetFullPath(dialog.FileName);
            string temporary = Path.Combine(Path.GetDirectoryName(target)!, ".visoloom-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(output.PngBytes);
                    file.Flush(true);
                }

                File.Move(temporary, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.Text = "The image could not be saved. Check the destination and free disk space.";
        }
    }
}

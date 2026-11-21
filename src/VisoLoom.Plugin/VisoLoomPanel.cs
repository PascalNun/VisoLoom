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
    private readonly bool connectAutomatically;
    private readonly DropDown model = new() { Width = 1 };
    private readonly Button projectModel = new() { Text = "Save model with project", Visible = false,
        ToolTip = "Store this model and version in the Rhino file. Save the file to keep the choice." };
    private readonly Expander projectOptions = new() { Header = "Project model", Expanded = false, Visible = false };
    private readonly DropDown preset = new() { Width = 1 };
    private readonly TextArea prompt = new() { Width = 1, Height = 58, ToolTip = "Optional instructions for this image" };
    private readonly WrappingLabel disclosure = CreateLabel("");
    private readonly Expander guidance = new() { Header = "Scene guidance", Expanded = false };
    private readonly WrappingLabel sceneContext = CreateLabel("");
    private readonly Button reviewPrompt = new() { Text = "View full prompt…", Enabled = false };
    private readonly Button refreshPreview = new() { Text = "Update source image", Visible = false };
    private readonly PanelImages images = new();
    private readonly ImageView symbol = new() { Width = 24, Height = 24 };
    private Bitmap? lightSymbol;
    private Bitmap? darkSymbol;
    private readonly List<StackLayout> fields = [];
    private readonly StackLayout representation;
    private readonly WrappingLabel promptLabel = CreateLabel("Prompt · optional");
    private readonly Button settings = new() { Text = "Settings…" };
    private readonly WrappingLabel viewLabel = CreateLabel("");
    private readonly DropDown sourceView = new() { Width = 1 };
    private readonly Button browseViews = new() { Text = "Named views…", ToolTip = "Browse Rhino named views" };
    private readonly Button showNamedView = new() { Text = "Show in Rhino", ToolTip = "Restore the selected named view in Rhino", Visible = false };
    private bool updatingControls;
    private bool updatingModels;
    private bool loaded;
    private bool customHeightEdited;
    private readonly WrappingLabel status = CreateLabel("");
    private readonly SegmentedButton resolution = new()
    {
        SelectionMode = SegmentedSelectionMode.Single,
        Items =
        {
            new ButtonSegmentedItem { Text = "Viewport", ToolTip = "Use the Rhino viewport size" },
            new ButtonSegmentedItem { Text = "2×", ToolTip = "Twice the viewport width and height" },
            new ButtonSegmentedItem { Text = "4×", ToolTip = "Four times the viewport width and height" },
            new ButtonSegmentedItem { Text = "Custom", ToolTip = "Set the size while keeping the viewport proportions" }
        },
        SelectedIndex = 0
    };
    private readonly TextBox width = new() { Text = "1920", Width = 80, ToolTip = "Width in pixels. Height follows the view's proportions. Press Enter or leave the field to apply." };
    private readonly TextBox height = new() { Text = "1080", Width = 80, ToolTip = "Height in pixels. Width follows the view's proportions. Press Enter or leave the field to apply." };
    private readonly WrappingLabel dimensions = CreateLabel("");
    private readonly Panel customSizeRow = new();
    private readonly Button primary = new() { Text = "Preview view", Font = SystemFonts.Bold(), Height = 34 };
    private readonly Scrollable scroll;
    private readonly Button cancel = new()
    {
        Text = "Cancel",
        Enabled = false
    };
    private readonly Button reconnect = new()
    {
        Text = "Reconnect",
        Visible = false
    };
    private readonly UITimer timer = new() { Interval = 1 };
    public VisoLoomPanel(RhinoDoc doc) : this(doc, CaptureCoordinator.ForDocument(doc.RuntimeSerialNumber), true) { }

    internal VisoLoomPanel(RhinoDoc doc, CaptureCoordinator session, bool connectAutomatically)
    {
        serial = doc.RuntimeSerialNumber;
        this.session = session;
        this.connectAutomatically = connectAutomatically;
        var header = new TableLayout
        {
            Padding = new Padding(10, 8, 10, 5),
            Spacing = new Size(6, 0),
            Rows = { new TableRow(symbol, new TableCell(CreateLabel("VisoLoom", bold: true), true), settings) }
        };
        var layout = new DynamicLayout { Padding = new Padding(10, 4, 10, 10), Spacing = new Size(6, 5) };
        foreach (var item in PromptPresets.All) preset.Items.Add(item.Name);
        preset.SelectedIndex = Math.Max(0, PromptPresets.All.ToList().FindIndex(p => p.Id == session.SelectedPreset.Id));
        prompt.Text = session.UserPrompt;
        var viewChoices = new StackLayout
        {
            Spacing = 4, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items = { new StackLayoutItem(sourceView, true), browseViews }
        };
        layout.AddRow(Field("View", viewChoices));
        layout.AddRow(showNamedView);
        layout.AddRow(viewLabel);
        representation = Field("Representation", preset);
        layout.AddRow(representation);
        layout.AddRow(Field("Model", model));
        projectOptions.Content = projectModel;
        layout.AddRow(projectOptions);
        layout.AddRow(promptLabel);
        layout.AddRow(prompt);
        layout.AddRow(CreateLabel("Output size", bold: true));
        layout.AddRow(resolution);
        customSizeRow.Content = new TableLayout
        {
            Spacing = new Size(6, 0),
            Rows = { new TableRow(width, new Label { Text = "×" }, height, new TableCell(null, true)) }
        };
        layout.AddRow(customSizeRow);
        layout.AddRow(dimensions);
        var advanced = new DynamicLayout { Padding = new Padding(8, 3, 0, 3), Spacing = new Size(6, 4) };
        advanced.AddRow(sceneContext);
        advanced.AddRow(reviewPrompt);
        advanced.AddRow(refreshPreview);
        guidance.Content = advanced;
        layout.AddRow(guidance);
        layout.AddRow(disclosure);
        layout.AddRow(new TableLayout
        {
            Spacing = new Size(5, 0),
            Rows = { new TableRow(new TableCell(primary, true), cancel) }
        });
        layout.AddRow(status);
        layout.AddRow(reconnect);
        layout.AddRow(images);
        scroll = new Scrollable { Content = layout, Border = BorderType.None, ExpandContentWidth = true };
        var shell = new DynamicLayout();
        shell.AddRow(header);
        shell.Add(scroll, xscale: true, yscale: true);
        Content = shell;
        SizeChanged += (_, _) =>
        {
            if (Size.Width <= 0) return;
            var orientation = Size.Width < 300 ? Orientation.Vertical : Orientation.Horizontal;
            foreach (var field in fields)
                if (field.Orientation != orientation) field.Orientation = orientation;
            if (viewChoices.Orientation != orientation) viewChoices.Orientation = orientation;
            images.SetPanelWidth(Size.Width);
        };
        lightSymbol = LoadSymbol("light");
        darkSymbol = LoadSymbol("dark");
        symbol.Image = lightSymbol;
        if (session.CustomSize is { } customSize)
        {
            width.Text = customSize.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            height.Text = customSize.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
            resolution.SelectedIndex = 3;
        }
        else
            resolution.SelectedIndex = session.Multiplier switch { 2 => 1, 4 => 2, _ => 0 };
        resolution.SelectedIndexChanged += (_, _) =>
        {
            if (updatingControls || resolution.SelectedIndex < 0)
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
        width.TextChanged += (_, _) =>
        {
            if (updatingControls)
                return;
            session.InvalidatePreview();
            customHeightEdited = false;
        };
        height.TextChanged += (_, _) =>
        {
            if (updatingControls)
                return;
            session.InvalidatePreview();
            customHeightEdited = true;
        };
        foreach (var input in new[] { width, height })
        {
            input.LostFocus += (_, _) => { ApplyCustomSize(); RefreshView(); };
            input.KeyDown += (_, e) =>
            {
                if (e.Key != Keys.Enter) return;
                e.Handled = true;
                ApplyCustomSize();
                settings.Focus();
                RefreshView();
            };
        }
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
        refreshPreview.Click += (_, _) =>
        {
            ApplyCustomSize();
            if (resolution.SelectedIndex == 3) settings.Focus();
            RefreshView();
            if (refreshPreview.Enabled && RhinoDoc.FromRuntimeSerialNumber(serial) is { } document)
                session.Preview(document);
        };
        reviewPrompt.Click += (_, _) =>
        {
            using var dialog = new Dialog { Title = "VisoLoom · Full prompt", ClientSize = new Size(560, 500), Resizable = true };
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
                // Account models have their own rows; choosing one names it as
                // the session's OpenAI model and keeps the provider selection.
                string key = model.SelectedKey ?? "local";
                if (key.StartsWith("openai:", StringComparison.Ordinal))
                    session.SelectedOpenAiModelId = key["openai:".Length..];
                else if (key.StartsWith("google:", StringComparison.Ordinal))
                    session.SelectedGoogleModelId = key["google:".Length..];
                else if (key.StartsWith("xai:", StringComparison.Ordinal))
                    session.SelectedXaiModelId = key["xai:".Length..];
                else if (key.StartsWith("bfl:", StringComparison.Ordinal))
                    session.SelectedBflModelId = key["bfl:".Length..];
                else
                    session.SelectedModelKey = key;
                Refresh();
                RefreshView();
            }
        };
        projectModel.Click += (_, _) =>
        {
            if (RhinoDoc.FromRuntimeSerialNumber(serial) is not { } document)
                return;
            if (session.UsingProjectPin)
                session.ClearProjectPin(document);
            else
                session.SetProjectPin(document);
        };
        ConnectionSettings.Changed += ConnectionChanged;
        UpdateModels();
        primary.Click += (_, _) =>
        {
            ApplyCustomSize();
            if (resolution.SelectedIndex == 3) settings.Focus();
            RefreshView();
            if (!primary.Enabled || RhinoDoc.FromRuntimeSerialNumber(serial) is not { } document) return;
            if (session.PreviewIsCurrent(document))
            {
                if (!images.IsShowingInput) images.SelectInput();
                else session.Start(document, session.SelectedPreset, prompt.Text);
            }
            else
                session.Preview(document);
        };
        reconnect.Click += (_, _) => session.Reconnect();
        cancel.Click += (_, _) => session.Cancel();
        session.Changed += SessionChanged;
        images.Picker.SelectedIndexChanged += (_, _) => RefreshView();
        foreach (var menu in new[] { sourceView, model, preset, images.Picker })
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
        timer.Elapsed += OnTimerElapsed;
        UnLoad += (_, _) => { loaded = false; timer.Stop(); };
        Load += (_, _) =>
        {
            loaded = true;
            timer.Start();
            Refresh();
        };
        Refresh();
    }

    private void OnTimerElapsed(object? sender, EventArgs e)
    {
        // The panel tears down on document close; a tick that still runs after
        // disposal would assign disposed bitmaps and take Rhino's runtime down.
        if (!loaded)
            return;
        RefreshView();
        if (connectAutomatically) _ = session.TryConnectAutomaticallyAsync();
    }

    private StackLayout Field(string name, Control control)
    {
        var label = CreateLabel(name);
        label.Width = 108;
        var field = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            AlignLabels = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items = { label, new StackLayoutItem(control, expand: true) }
        };
        fields.Add(field);
        return field;
    }

    private static Bitmap? LoadSymbol(string appearance)
    {
        using var resource = typeof(VisoLoomPanel).Assembly.GetManifestResourceStream($"VisoLoom.Plugin.Resources.PanelSymbol-{appearance}.png");
        return resource is null ? null : new Bitmap(resource);
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
            timer.Elapsed -= OnTimerElapsed;
            timer.Stop();
            timer.Dispose();
            symbol.Image = null;
            lightSymbol?.Dispose();
            darkSymbol?.Dispose();
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
        var choices = new List<(string Key, string Text)>
        {
            ("local", "Capture test · local")
        };
        // The account list is the source: each image model it offered at the
        // last connection check gets a row, newest first; the tested compiled
        // profile is named as such and cannot silently change.
        if (ConnectionSettings.IsReady)
        {
            var listed = ConnectionSettings.AvailableOpenAiModels;
            if (listed.Count == 0)
                choices.Add(("openai", OpenAiImageProvider.DisplayName + " · cloud"));
            else
                foreach (var available in listed)
                    choices.Add(("openai:" + available.Id, available.Id == OpenAiImageProvider.ModelId
                        ? OpenAiImageProvider.DisplayName + " · tested"
                        : available.DisplayName + " · cloud"));
        }
        else
            choices.Add(("openai", OpenAiImageProvider.DisplayName + " · needs setup"));
        if (ConnectionSettings.GoogleIsReady)
        {
            var listedGoogle = ConnectionSettings.AvailableGoogleModels;
            if (listedGoogle.Count == 0)
                choices.Add(("google", GoogleImageProvider.AccountModel(GoogleImageProvider.ModelId).DisplayName + " · cloud"));
            else
                foreach (var available in listedGoogle)
                    choices.Add(("google:" + available.Id, available.Id == GoogleImageProvider.ModelId
                        ? available.DisplayName + " · tested"
                        : available.DisplayName + " · cloud"));
        }
        else
            choices.Add(("google", GoogleImageProvider.AccountModel(GoogleImageProvider.ModelId).DisplayName + " · needs setup"));
        if (ConnectionSettings.XaiIsReady)
        {
            var listedXai = ConnectionSettings.AvailableXaiModels;
            if (listedXai.Count == 0)
                choices.Add(("xai", XaiImageProvider.AccountModel(XaiImageProvider.ModelId).DisplayName + " · cloud"));
            else
                foreach (var available in listedXai)
                    choices.Add(("xai:" + available.Id, available.Id == XaiImageProvider.ModelId
                        ? available.DisplayName + " · tested"
                        : available.DisplayName + " · cloud"));
        }
        else
            choices.Add(("xai", XaiImageProvider.AccountModel(XaiImageProvider.ModelId).DisplayName + " · needs setup"));
        if (ConnectionSettings.BflIsReady)
        {
            var listedBfl = ConnectionSettings.AvailableBflModels;
            if (listedBfl.Count == 0)
                choices.Add(("bfl", BflImageProvider.AccountModel(BflImageProvider.ModelId).DisplayName + " · curated"));
            else
                foreach (var available in listedBfl)
                    choices.Add(("bfl:" + available.Id, available.Id == BflImageProvider.ModelId
                        ? available.DisplayName + " · curated"
                        : available.DisplayName + " · latest"));
        }
        else
            choices.Add(("bfl", BflImageProvider.AccountModel(BflImageProvider.ModelId).DisplayName + " · needs setup"));
        if (ConnectionSettings.LocalEngineAllowed && session.LocalModel is { } local)
            choices.Add(("managed", local.DisplayName));
        else if (session.SelectedModelKey == "managed")
            choices.Add(("managed", "Local model · needs setup"));
        if (session.ProjectPin is { } pin)
        {
            string name = pin.ProviderId == "openai" && pin.ModelId == OpenAiImageProvider.ModelId
                ? OpenAiImageProvider.DisplayName
                : pin.ProviderId == "openai" && OpenAiImageProvider.IsFamilyModel(pin.ModelId)
                    ? OpenAiImageProvider.AccountModel(pin.ModelId).DisplayName
                : pin.ProviderId == "google" && GoogleImageProvider.IsImageModel(pin.ModelId)
                    ? GoogleImageProvider.AccountModel(pin.ModelId).DisplayName
                : pin.ProviderId == "xai" && XaiImageProvider.IsImageModel(pin.ModelId)
                    ? XaiImageProvider.AccountModel(pin.ModelId).DisplayName
                : pin.ProviderId == "bfl" && BflImageProvider.IsCuratedModel(pin.ModelId)
                    ? BflImageProvider.AccountModel(pin.ModelId).DisplayName
                : pin.ProviderId == "local" ? VisoLoom.Protocol.ManagedModels.Find(pin.ModelId)?.DisplayName ?? pin.ModelId : pin.ModelId;
            choices.Add((CaptureCoordinator.ProjectModelKey, name + " · project model"));
        }
        string current = session.UsingProjectPin ? CaptureCoordinator.ProjectModelKey
            : session.SelectedProvider == "openai"
                ? "openai:" + (session.SelectedOpenAiModelId.Length > 0 ? session.SelectedOpenAiModelId : OpenAiImageProvider.ModelId)
            : session.SelectedProvider == "google"
                ? "google:" + (session.SelectedGoogleModelId.Length > 0 ? session.SelectedGoogleModelId : GoogleImageProvider.ModelId)
            : session.SelectedProvider == "xai"
                ? "xai:" + (session.SelectedXaiModelId.Length > 0 ? session.SelectedXaiModelId : XaiImageProvider.ModelId)
            : session.SelectedProvider == "bfl"
                ? "bfl:" + (session.SelectedBflModelId.Length > 0 ? session.SelectedBflModelId : BflImageProvider.ModelId)
                : session.SelectedModelKey;
        if (model.SelectedKey == current &&
            model.Items.Select(item => (item.Key, item.Text)).SequenceEqual(choices))
            return;
        updatingModels = true;
        try
        {
            model.Items.Clear();
            foreach (var choice in choices)
                model.Items.Add(new ListItem { Key = choice.Key, Text = choice.Text });
            model.SelectedKey = current;
        }
        finally { updatingModels = false; }
    }

    private void SessionChanged(object? sender, EventArgs e)
    {
        UpdateModels();
        Refresh();
    }
    private void Refresh()
    {
        status.Text = session.Status;
        reconnect.Visible = session.CanReconnect;
        reconnect.Enabled = !session.IsBusy;
        bool generating = session.SelectedProvider != "local";
        disclosure.Text = session.SelectedProvider switch
        {
            "managed" => "Renders on this computer.",
            "openai" => "Sends the source image and scene guidance to OpenAI.",
            "google" => "Sends the source image and scene guidance to Google.",
            "xai" => "Sends the source image and scene guidance to xAI.",
            "bfl" => "Nothing is sent to BFL until its reference-upload contract is resolved.",
            "local" => "Capture test only. No image generation.",
            _ => ""
        };
        cancel.Text = session.ActiveProvider is "openai" or "google" or "xai" ? "Stop waiting" : "Cancel";
        settings.Enabled = !session.IsBusy;
        cancel.Enabled = cancel.Visible = session.ActiveProvider is not null;
        reviewPrompt.Visible = session.Intent is { EffectivePrompt.Length: > 0 };
        reviewPrompt.Enabled = !session.IsBusy;
        resolution.Enabled = sourceView.Enabled = showNamedView.Enabled = browseViews.Enabled = model.Enabled = !session.IsBusy;
        projectOptions.Visible = projectModel.Visible = session.UsingProjectPin || session.SelectedModel is { Capabilities.IsDevelopmentPassthrough: false };
        projectModel.Enabled = !session.IsBusy && RhinoDoc.FromRuntimeSerialNumber(serial) is { IsReadOnly: false };
        projectModel.Text = session.UsingProjectPin ? "Remove saved model" : "Save model with project";
        preset.Enabled = prompt.Enabled = generating && !session.IsBusy;
        representation.Visible = promptLabel.Visible = prompt.Visible = generating;
        PanelInput? prepared = null;
        if (session.Capture is { } captured && session.PreviewPng is { } png)
        {
            var frame = session.PreviewPlan?.Native ?? captured.Manifest.Frame;
            prepared = new(captured.Manifest.SceneId, png, frame,
                $"{captured.ViewName}\n{captured.DisplayStyle}\n{session.PreviewModelName}\n{frame.Width:N0} × {frame.Height:N0} px · {captured.Manifest.CapturedAt.ToLocalTime():t}");
            sceneContext.Text = session.SelectedProvider == "bfl"
                ? "Nothing leaves this computer for BFL while its reference-upload contract is unresolved."
                : session.SelectedProvider is "openai" or "google" or "xai"
                ? "Camera, framing, section and material context accompany the source image. View the full prompt to inspect it."
                : session.SelectedProvider == "managed" ? "The local model uses the source image and prompt."
                : "Capture test returns the source image without image generation.";
            guidance.Header = captured.Manifest.Clipping.Any(clip => clip.Enabled && clip.AppliesToView == true)
                ? "Scene guidance · Section" : "Scene guidance";
        }
        else
        {
            sceneContext.Text = "Preview the view to inspect its scene guidance.";
            guidance.Header = "Scene guidance";
        }
        images.Update(prepared, session.RecentRenders);
        RefreshView();
    }

    private void RefreshView()
    {
        var document = RhinoDoc.FromRuntimeSerialNumber(serial);
        var text = SystemColors.ControlText;
        var brand = text.R + text.G + text.B > 1.5f ? darkSymbol : lightSymbol;
        if (symbol.Image != brand) symbol.Image = brand;
        bool custom = resolution.SelectedIndex == 3;
        customSizeRow.Visible = custom;
        width.Enabled = height.Enabled = custom && !session.IsBusy;
        // Native text fields must own their text while editing. Polling and
        // linked-aspect updates previously replaced every partially typed value.
        if (custom && (width.HasFocus || height.HasFocus)) return;
        if (document is null)
        {
            viewLabel.Text = "No model viewport";
            DisablePreview("Open a model viewport to preview it.");
            return;
        }
        UpdateViews(document);
        showNamedView.Visible = session.NamedView.Name is not null;
        try
        {
            var view = session.SourceView(document);
            var viewport = view.ActiveViewport;
            string projection = viewport.IsParallelProjection ? "Parallel"
                : viewport.IsTwoPointPerspectiveProjection ? "Two-point perspective" : "Perspective";
            viewLabel.Text = (viewport.Name == projection ? projection : viewport.Name + " · " + projection) + " · " + viewport.DisplayMode.LocalName;
            var pixels = new PixelSize(viewport.Size.Width, viewport.Size.Height);
            var multiplier = resolution.SelectedIndex switch
            {
                1 => 2,
                2 => 4,
                _ => 1
            };
            if (!session.IsBusy)
            {
                session.Multiplier = multiplier;
            }
            var size = custom
                ? ReadCustomSize(pixels)
                : ResolutionPlanner.ScaleViewport(pixels, multiplier);
            if (!session.IsBusy)
            {
                SetCustomSize(size);
                session.CustomSize = custom ? size : null;
                session.Multiplier = multiplier;
            }
            string sizeText = $"{size.Width:N0} × {size.Height:N0} px";
            string sizeHelp = "Saved image size. The complete view is kept at one uniform scale; small borders can result from whole-pixel rounding.";
            if (session.SelectedModel is not { } selectedModel)
            {
                dimensions.Text = sizeText;
                DisablePreview(session.ModelUnavailableMessage);
                return;
            }
            if (!selectedModel.Capabilities.IsDevelopmentPassthrough)
            {
                var plan = selectedModel.Plan(ResolutionPlanner.CaptureSize(pixels, size), size);
                sizeHelp += $" Model canvas: {plan.Native.Width:N0} × {plan.Native.Height:N0} pixels.";
                if (plan.HasPadding)
                {
                    sizeHelp += " The surrounding padding is removed for export.";
                }
                if (plan.RequiresResampling)
                {
                    sizeText += $"\nGenerated area · {plan.ContentRectangle.Width:N0} × {plan.ContentRectangle.Height:N0} px · resized for export";
                    sizeHelp += $" The model paints {plan.ContentRectangle.Width:N0} × {plan.ContentRectangle.Height:N0} content pixels before export scaling.";
                }
                if (session.SelectedProvider == "managed" || plan.Native.Pixels > 2560L * 1440)
                    sizeHelp += " This model's resolution support is experimental.";
                if (session.SelectedProvider == "openai" && plan.Native.Pixels > 2560L * 1440)
                    sizeText += "\nExperimental image size";
            }
            dimensions.Text = sizeText;
            if (dimensions.ToolTip != sizeHelp) dimensions.ToolTip = sizeHelp;
            if (images.InputFailed) session.InvalidatePreview();
            bool current = !session.IsBusy && session.PreviewIsCurrent(document);
            bool hasInput = session.PreviewPng is not null;
            bool reviewInput = current && !images.IsShowingInput;
            primary.Text = session.IsBusy ? "Working…" : current
                ? reviewInput ? "Review source" : session.SelectedProvider == "local" ? "Test capture" : "Render view"
                : hasInput ? "Update preview" : "Preview view";
            primary.Enabled = !session.IsBusy && (!current || reviewInput || !session.IsConnecting && session.IsConfigured);
            primary.ToolTip = current ? reviewInput ? "Return to the reviewed source image." : "Send the reviewed source image to the selected model."
                : "Capture the selected Rhino view locally for review.";
            refreshPreview.Visible = hasInput;
            refreshPreview.Enabled = !session.IsBusy;
            status.Text = session.Status;
            if (!session.IsBusy && session.StatusIsGuidance)
            {
                status.Text = hasInput && !current ? "The view or settings changed. Update the preview." :
                    "";
                if (!session.IsConfigured)
                    status.Text = session.SelectedProvider == "local" ? "Start the local service to test capture." :
                        "Connect this model in Settings to render.";
            }
            status.Visible = status.Text.Length != 0;
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            dimensions.Text = ex is OverflowException ? "Choose a smaller image size." : ex.Message;
            DisablePreview("Enter a supported size to preview the view.");
        }
        catch (InvalidOperationException ex)
        {
            viewLabel.Text = ex.Message;
            dimensions.Text = "";
            DisablePreview("Choose or show a supported view, then preview again.");
        }
    }

    private void DisablePreview(string message)
    {
        session.InvalidatePreview();
        primary.Enabled = refreshPreview.Enabled = false;
        primary.Text = session.PreviewPng is null ? "Preview view" : "Update preview";
        status.Text = message;
        status.Visible = true;
    }

    private void SetCustomSize(PixelSize size)
    {
        updatingControls = true;
        try
        {
            string w = size.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string h = size.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (width.Text != w) width.Text = w;
            if (height.Text != h) height.Text = h;
        }
        finally { updatingControls = false; }
    }

    private PixelSize ReadCustomSize(PixelSize viewport)
    {
        string text = customHeightEdited ? height.Text : width.Text;
        if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value) || value is < 1 or > 16384)
            throw new ArgumentException("Enter a whole number from 1 to 16,384 pixels.");
        return customHeightEdited ? ResolutionPlanner.OutputHeight(viewport, value) : ResolutionPlanner.OutputWidth(viewport, value);
    }

    private void ApplyCustomSize()
    {
        if (resolution.SelectedIndex != 3 || session.IsBusy || RhinoDoc.FromRuntimeSerialNumber(serial) is not { } document) return;
        try
        {
            var size = ReadCustomSize(SceneBuilder.ViewportSize(session.SourceView(document)));
            SetCustomSize(size);
            session.CustomSize = size;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            dimensions.Text = ex.Message;
            DisablePreview("Enter a supported size to preview the view.");
        }
    }

    private void UpdateViews(RhinoDoc document)
    {
        var views = SceneBuilder.ModelViews(document);
        var items = new List<ListItem> { new() { Key = "active", Text = "Current view" } };
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
            if (loaded)
                timer.Start();
        }
    }

}

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
    private readonly PanelChoicePicker model = new();
    private readonly PanelChoicePicker preset = new();
    private readonly Button editPreset = new() { Text = "Edit preset…", ToolTip = "Edit the selected preset or save a personal version" };
    private readonly TextArea prompt = new() { Width = 1, Height = 46, ToolTip = "Optional instructions for this image" };
    private readonly WrappingLabel disclosure = CreateLabel("");
    private readonly Expander guidance = new() { Header = "Scene guidance", Expanded = false };
    private readonly WrappingLabel sceneContext = CreateLabel("");
    private readonly Button reviewPrompt = new() { Text = "View full prompt…", Enabled = false };
    private readonly Button refreshPreview = new() { Text = "Update source image", Visible = false };
    private readonly PanelImages images;
    // The honest render bar: a local run fills it by the sampler's own steps,
    // a cloud run moves it — a synchronous call reports no percentage, and
    // the bar never pretends otherwise.
    private readonly ProgressBar renderBar = new() { MinValue = 0, MaxValue = 1000, Height = 10, Visible = false };
    // The local model's live preview while its runner writes it; absent for
    // every cloud, hidden whenever no readable frame exists.
    private readonly ImageView livePreview = new() { Visible = false };
    // The sampling dial: the review recipe is the model default, and the
    // choices come from the selected local profile's own bounds.
    private readonly DropDown samplingSteps = new() { Width = 108, ToolTip = "Sampling steps for this render. The model default is the reviewed recipe." };
    private VisoLoom.Protocol.ManagedModelDefinition? stepsDefinition;
    private bool updatingSteps;
    private readonly ImageView symbol = new() { Width = 24, Height = 24 };
    private Bitmap? lightSymbol;
    private Bitmap? darkSymbol;
    private readonly List<StackLayout> fields = [];
    private readonly StackLayout representation;
    private readonly StackLayout sampling;
    private readonly StackLayout setup;
    private readonly Button toggleSetup = new() { Text = "Hide setup", Visible = false };
    private readonly WrappingLabel promptLabel = CreateLabel("Prompt · optional");
    private readonly Button settings = new() { Text = "Settings…" };
    private readonly WrappingLabel viewLabel = CreateLabel("");
    private readonly DropDown sourceView = new() { Width = 1 };
    private readonly Button browseViews = new() { Text = "Named views…", ToolTip = "Browse Rhino named views" };
    private readonly Button showNamedView = new() { Text = "Show in Rhino", ToolTip = "Restore the selected named view in Rhino", Visible = false };
    private bool updatingControls;
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
    private readonly CheckBox lockRatio = new() { Text = "Lock", Checked = true, ToolTip = "Locked: the typed dimension keeps the view's proportions (ADR 0023). Unlocked: both dimensions are typed freely and their ratio frames a centered safe frame inside the view (ADR 0030)." };
    private readonly WrappingLabel dimensions = CreateLabel("");
    private readonly WrappingLabel sizeDetails = CreateLabel("");
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
            Rows = { new TableRow(symbol, new TableCell(CreateLabel("Render", bold: true), true), settings) }
        };
        setup = SettingsLayout.Stack();
        setup.Spacing = 4;
        images = new PanelImages(toggleSetup);
        UpdatePresetChoices();
        prompt.Text = session.UserPrompt;
        var viewChoices = new StackLayout
        {
            Orientation = Orientation.Horizontal, Spacing = 4,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center,
            Items = { new StackLayoutItem(sourceView, true), browseViews }
        };
        var viewControl = SettingsLayout.Stack(viewChoices, viewLabel, showNamedView);
        viewControl.Spacing = 2;
        setup.Items.Add(Field("View", viewControl));
        var representationChoices = new StackLayout
        {
            Orientation = Orientation.Horizontal, Spacing = 4,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center,
            Items = { new StackLayoutItem(preset, true), editPreset }
        };
        representation = Field("Representation", representationChoices);
        setup.Items.Add(representation);
        setup.Items.Add(preset.Options);
        setup.Items.Add(Field("Model", model));
        setup.Items.Add(model.Options);
        sampling = Field("Sampling steps", samplingSteps);
        sampling.Visible = false;
        setup.Items.Add(sampling);
        setup.Items.Add(promptLabel);
        setup.Items.Add(prompt);
        setup.Items.Add(CreateLabel("Output size", bold: true));
        var outputSize = new StackLayout
        {
            Orientation = Orientation.Vertical, Spacing = 5,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items = { resolution, new StackLayoutItem(dimensions, true) }
        };
        setup.Items.Add(outputSize);
        customSizeRow.Content = new TableLayout
        {
            Spacing = new Size(6, 0),
            Rows = { new TableRow(width, new Label { Text = "×" }, height, lockRatio) }
        };
        setup.Items.Add(customSizeRow);
        setup.Items.Add(sizeDetails);
        var advanced = new DynamicLayout { Padding = new Padding(8, 3, 0, 3), Spacing = new Size(6, 4) };
        advanced.AddRow(sceneContext);
        advanced.AddRow(reviewPrompt);
        advanced.AddRow(refreshPreview);
        guidance.Content = advanced;
        setup.Items.Add(guidance);
        var layout = new DynamicLayout { Padding = new Padding(10, 4, 10, 10), Spacing = new Size(6, 5) };
        layout.AddRow(setup);
        layout.AddRow(SettingsLayout.Rule());
        layout.AddRow(disclosure);
        layout.AddRow(new TableLayout
        {
            Spacing = new Size(5, 0),
            Rows = { new TableRow(new TableCell(primary, true), cancel) }
        });
        var feedback = SettingsLayout.Stack(status, renderBar, livePreview, reconnect);
        feedback.Spacing = 3;
        layout.AddRow(feedback);
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
            if (representationChoices.Orientation != orientation) representationChoices.Orientation = orientation;
            var sizeOrientation = Size.Width >= 420 ? Orientation.Horizontal : Orientation.Vertical;
            if (outputSize.Orientation != sizeOrientation) outputSize.Orientation = sizeOrientation;
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
        lockRatio.Checked = session.LinkedRatio;
        UpdateLockTooltips(session.LinkedRatio);
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
        // ADR 0030's per-session lock: on, the typed dimension keeps the view's
        // proportions; off, both dimensions are typed freely and their ratio
        // frames a centered safe frame inside the view.
        lockRatio.CheckedChanged += (_, _) =>
        {
            if (updatingControls)
                return;
            bool linked = lockRatio.Checked == true;
            session.LinkedRatio = linked;
            session.InvalidatePreview();
            UpdateLockTooltips(linked);
            ApplyCustomSize();
            RefreshView();
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
        settings.Click += (_, _) => ConnectionSettingsControl.ShowSettings(this);
        editPreset.Click += (_, _) =>
        {
            preset.Dismiss();
            model.Dismiss();
            PromptPresetEditor.Show(this, session, RefreshPresets);
        };
        model.Opened += preset.Dismiss;
        preset.Opened += model.Dismiss;
        toggleSetup.Click += (_, _) =>
        {
            setup.Visible = !setup.Visible;
            toggleSetup.Text = setup.Visible ? "Hide setup" : "Show setup";
            if (!setup.Visible)
            {
                model.Dismiss();
                preset.Dismiss();
            }
        };
        preset.ChoiceSelected += id =>
        {
            var choice = PromptPresets.All.FirstOrDefault(p => p.Id == id);
            if (choice is not null)
                SelectPreset(choice);
        };
        prompt.TextChanged += (_, _) =>
        {
            session.InvalidatePreview();
            session.UserPrompt = prompt.Text;
            RefreshView();
        };
        samplingSteps.SelectedIndexChanged += (_, _) =>
        {
            if (updatingSteps || samplingSteps.SelectedIndex < 0) return;
            session.InvalidatePreview();
            session.SamplingSteps = samplingSteps.SelectedIndex == 0 || stepsDefinition is null
                ? null
                : stepsDefinition.MinSteps + samplingSteps.SelectedIndex - 1;
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
        model.ChoiceSelected += key =>
        {
            if (RhinoDoc.FromRuntimeSerialNumber(serial) is not { } document)
                return;
            session.ChooseModel(document, key);
            UpdateModels();
            Refresh();
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
        images.SelectionChanged += (_, _) => RefreshView();
        foreach (var menu in new[] { sourceView, samplingSteps })
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
            // Covers a start-time refresh that could not reach the network:
            // the panel open retries once a session has not yet listed.
            AccountListRefresh.RunOnce();
        };
        Refresh();
        // A personal-preset file that could not be read is named once here;
        // the supplied presets keep working and the editor shows the same reason.
        if (PromptPresets.Problem is { Length: > 0 } problem)
        {
            status.Text = problem;
            status.Visible = true;
        }
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

    // Rebuilt after a personal preset is saved, copied or deleted: the choices
    // come from the edited store, the affected preset lands selected, and the
    // fallback after a deletion is the user's persisted default choice.
    private void RefreshPresets(string? selectId)
    {
        PromptPresets.Refresh();
        SelectPreset(PromptPresets.All.FirstOrDefault(choice => choice.Id == selectId) ?? PromptPresets.DefaultChoice());
    }

    private void SelectPreset(RepresentationPreset choice)
    {
        if (session.SelectedPreset != choice)
            session.InvalidatePreview();
        session.SelectedPreset = choice;
        PromptPresets.RememberDefault(choice);
        UpdatePresetChoices();
        RefreshView();
    }

    private void UpdatePresetChoices()
    {
        var choices = PromptPresets.All.Select(p => new PanelChoice(p.Id,
            PromptPresets.IsPersonal(p) ? "My presets" : "Built-in presets", p.Name, "", Description: p.Description)).ToArray();
        var current = choices.FirstOrDefault(choice => choice.Key == session.SelectedPreset.Id)
            ?? new(session.SelectedPreset.Id, "", session.SelectedPreset.Name, "");
        preset.Update(choices, current);
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
        var choices = new List<PanelChoice>();
        foreach (var local in session.ManagedModels)
            choices.Add(new("managed:" + local.Id, "This computer", ModelName(local.DisplayName), "This computer",
                ModelId: local.Id, Version: local.Version));
        if (session.ManagedModels.Count == 0)
            choices.Add(new("managed", "This computer", "Local models", "This computer", "Needs setup"));

        AddCloud("openai", "OpenAI", OpenAiImageProvider.AccountModel(OpenAiImageProvider.ModelId),
            ConnectionSettings.IsReady, ConnectionSettings.AvailableOpenAiModels);
        AddCloud("google", "Google", GoogleImageProvider.AccountModel(GoogleImageProvider.ModelId),
            ConnectionSettings.GoogleIsReady, ConnectionSettings.AvailableGoogleModels);
        AddCloud("xai", "xAI", XaiImageProvider.AccountModel(XaiImageProvider.ModelId),
            ConnectionSettings.XaiIsReady, ConnectionSettings.AvailableXaiModels);
        AddCloud("bfl", "Black Forest Labs", BflImageProvider.AccountModel(BflImageProvider.ModelId),
            ConnectionSettings.BflIsReady, ConnectionSettings.AvailableBflModels.Where(model => BflImageProvider.IsCuratedModel(model.Id)));

        var profile = session.SelectedModel;
        choices.Add(new("local", "Diagnostics", "Capture test", "This computer"));

        string providerKey = session.SelectedProvider;
        string currentKey = profile is not null && providerKey != "local"
            ? providerKey + ":" + profile.Capabilities.Id : session.SelectedModelKey;
        var current = choices.Find(choice => choice.Key == currentKey)
            ?? new(currentKey, "", ModelName(profile?.Capabilities.DisplayName ?? currentKey), "Model", "Unavailable");
        if (session.UsingProjectPin && session.ProjectPin is { } saved && (profile is null || !choices.Any(choice => choice.Key == currentKey)))
        {
            string destination = saved.ProviderId switch
            {
                "local" => "This computer",
                "openai" => "Cloud · OpenAI",
                "google" => "Cloud · Google",
                "xai" => "Cloud · xAI",
                "bfl" => "Cloud · Black Forest Labs",
                _ => "Model"
            };
            current = new("", "", PinnedModelName(saved), destination, "Unavailable", ModelId: saved.ModelId, Version: saved.ModelVersion);
        }
        if (session.SelectedProvider == "local" && !session.CaptureTestSelected)
            current = current with { Key = "", Name = "Choose a model…", Destination = "" };
        model.Update(choices, current);
        if (stepsDefinition?.Id != (session.SelectedProvider == "managed" ? profile?.Capabilities.Id : null))
            RefreshSteps();

        void AddCloud(string key, string provider, ModelCapabilities fallback, bool ready,
            IEnumerable<ModelCapabilities> available)
        {
            var profiles = ready ? available.DefaultIfEmpty(fallback) : [fallback];
            foreach (var capability in profiles)
            {
                string name = ModelDisplayName(capability.Id, capability.DisplayName);
                choices.Add(new(key + ":" + capability.Id, "Cloud", name, "Cloud · " + provider,
                    ready ? "" : "Needs setup", provider, ModelId: capability.Id, Version: capability.Version));
            }
        }
    }

    private static string ModelName(string name) => name
        .Replace(" · this computer", "", StringComparison.OrdinalIgnoreCase)
        .Replace("OpenAI · ", "", StringComparison.Ordinal)
        .Replace("Google · ", "", StringComparison.Ordinal)
        .Replace("xAI · ", "", StringComparison.Ordinal)
        .Replace("BFL · ", "", StringComparison.Ordinal);

    private static string ModelDisplayName(string id, string displayName) => id switch
    {
        "gemini-2.5-flash-image" => "Gemini 2.5 Flash Image",
        "gemini-3.1-flash-image" => "Nano Banana 2 · Gemini 3.1 Flash Image",
        "gemini-3.1-flash-lite-image" => "Nano Banana 2 Lite · Gemini 3.1 Flash Lite Image",
        _ when id == OpenAiImageProvider.ModelId => $"GPT Image 2.5 Sunburst · {id[^10..]}",
        _ => ModelName(displayName)
    };

    private static string PinnedModelName(ProjectModelPin pin) => ModelDisplayName(pin.ModelId, pin.ProviderId switch
    {
        "local" => VisoLoom.Protocol.ManagedModels.Find(pin.ModelId)?.DisplayName ?? pin.ModelId,
        "openai" when OpenAiImageProvider.IsFamilyModel(pin.ModelId) => OpenAiImageProvider.AccountModel(pin.ModelId).DisplayName,
        "google" when GoogleImageProvider.IsImageModel(pin.ModelId) => GoogleImageProvider.AccountModel(pin.ModelId).DisplayName,
        "xai" when XaiImageProvider.IsImageModel(pin.ModelId) => XaiImageProvider.AccountModel(pin.ModelId).DisplayName,
        "bfl" when BflImageProvider.IsCuratedModel(pin.ModelId) => BflImageProvider.AccountModel(pin.ModelId).DisplayName,
        _ => pin.ModelId
    });

    private void SessionChanged(object? sender, EventArgs e)
    {
        UpdateModels();
        Refresh();
    }

    // The sampling dial: the selected local profile's reviewed recipe is the
    // "model default", and the dial's other choices are that profile's own
    // bounds — a cloud model renders on its provider's fixed contract, so
    // the dial hides. The standing choice survives a redraw when it still
    // fits the profile; otherwise the recipe returns.
    private void RefreshSteps()
    {
        var definition = session.SelectedProvider == "managed" && session.SelectedModel?.Capabilities is { } local
            ? VisoLoom.Protocol.ManagedModels.Find(local.Id)
            : null;
        stepsDefinition = definition;
        updatingSteps = true;
        try
        {
            samplingSteps.Items.Clear();
            if (definition is not null)
            {
                samplingSteps.Items.Add($"Model default · {definition.RecipeSteps}");
                for (int value = definition.MinSteps; value <= definition.MaxSteps; value++)
                    samplingSteps.Items.Add(value == definition.RecipeSteps ? $"{value} · recipe" : value.ToString());
            }
            if (session.SamplingSteps is { } chosen && definition is not null &&
                (chosen < definition.MinSteps || chosen > definition.MaxSteps))
                session.SamplingSteps = null;
            samplingSteps.SelectedIndex = session.SamplingSteps is { } steps && definition is not null
                ? 1 + steps - definition.MinSteps
                : 0;
        }
        finally
        {
            updatingSteps = false;
            sampling.Visible = definition is not null;
        }
    }

    // Draws the live render feedback: the bar fills from the local sampler's
    // own steps and moves for a cloud — a synchronous call reports no
    // percentage, so the seconds waited run beside the phase sentence. The
    // local model's current preview frame appears while its worker writes it.
    private void DrawRenderProgress()
    {
        var live = session.LiveProgress;
        bool busy = session.IsBusy;
        renderBar.Visible = busy;
        renderBar.Indeterminate = busy && live?.Step is null;
        if (busy && live?.Step is { } step && live.Steps is { } total && total > 0)
        {
            renderBar.Indeterminate = false;
            renderBar.Value = (int)Math.Clamp(step * 1000L / total, 0, 1000);
        }
        if (busy && session.RenderStarted is { } started && live?.EtaSeconds is null)
            status.Text = session.Status.TrimEnd('…') + " · " +
                Math.Max(0, (int)(DateTime.UtcNow - started).TotalSeconds) + " s…";
        DrawLivePreview(live?.PreviewPath);
    }

    private void DrawLivePreview(string? path)
    {
        try
        {
            if (path is null || !File.Exists(path) || new FileInfo(path).Length is 0 or > 16_777_216)
            {
                HideLivePreview();
                return;
            }
            using var stream = File.OpenRead(path);
            using var frame = new Bitmap(stream);
            // A proportional thumbnail: the live frame is a progress signal,
            // the completed result remains the record.
            double scale = Math.Min(1.0, Math.Min(320d / frame.Width, 180d / frame.Height));
            var thumbnail = new Bitmap(
                new Size((int)Math.Max(1, frame.Width * scale), (int)Math.Max(1, frame.Height * scale)),
                PixelFormat.Format32bppRgba);
            using (var surface = new Graphics(thumbnail))
                surface.DrawImage(frame, 0, 0, thumbnail.Width, thumbnail.Height);
            livePreview.Image?.Dispose();
            livePreview.Image = thumbnail;
            livePreview.Visible = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            // The worker rewrites the file while sampling; a torn read keeps
            // the previous frame for one more tick instead of raising.
        }
    }

    private void HideLivePreview()
    {
        if (livePreview.Image is { } old)
        {
            livePreview.Image = null;
            old.Dispose();
        }
        livePreview.Visible = false;
    }
    private void Refresh()
    {
        status.Text = session.Status;
        reconnect.Visible = session.CanReconnect;
        reconnect.Enabled = !session.IsBusy;
        bool captureTest = session.SelectedProvider == "local" && session.CaptureTestSelected;
        bool generating = !captureTest;
        samplingSteps.Enabled = !session.IsBusy;
        if (session.IsBusy)
        {
            model.Dismiss();
            preset.Dismiss();
        }
        disclosure.Text = session.SelectedProvider switch
        {
            "managed" => "",
            "openai" => "Sends the source image and scene guidance to OpenAI.",
            "google" => "Sends the source image and scene guidance to Google.",
            "xai" => "Sends the source image and scene guidance to xAI.",
            "bfl" => "Sends the source image and scene guidance to Black Forest Labs.",
            "local" when captureTest => "Capture test returns the source image.",
            _ => ""
        };
        disclosure.Visible = disclosure.Text.Length != 0;
        cancel.Text = session.ActiveProvider is "openai" or "google" or "xai" or "bfl" ? "Stop waiting" : "Cancel";
        settings.Enabled = !session.IsBusy;
        cancel.Enabled = cancel.Visible = session.ActiveProvider is not null;
        reviewPrompt.Visible = session.Intent is { EffectivePrompt.Length: > 0 };
        reviewPrompt.Enabled = !session.IsBusy;
        resolution.Enabled = sourceView.Enabled = showNamedView.Enabled = browseViews.Enabled = model.Enabled = !session.IsBusy;
        preset.Enabled = editPreset.Enabled = prompt.Enabled = generating && !session.IsBusy;
        representation.Visible = promptLabel.Visible = prompt.Visible = generating;
        if (!generating) preset.Dismiss();
        preset.Options.Enabled = generating && !session.IsBusy;
        UpdatePresetChoices();
        PanelInput? prepared = null;
        if (session.Capture is { } captured && session.PreviewPng is { } png)
        {
            var frame = session.PreviewPlan?.Native ?? captured.Manifest.Frame;
            prepared = new(captured.Manifest.SceneId, png, frame,
                $"{captured.ViewName}\n{captured.DisplayStyle}\n{session.PreviewModelName}\n{frame.Width:N0} × {frame.Height:N0} px · {captured.Manifest.CapturedAt.ToLocalTime():t}");
        }
        sceneContext.Text = session.SelectedProvider switch
        {
            "managed" => "Uses the Rhino capture and the composed prompt on this computer.",
            "openai" or "google" or "xai" or "bfl" => "Uses the Rhino capture, composed prompt, camera, framing, units, active clipping planes and material context. Names depend on your connection settings.",
            "local" when captureTest => "Returns the captured image.",
            _ => "Choose a model to see what it uses."
        };
        string guidanceTitle = session.SelectedProvider switch
        {
            "managed" => "Scene guidance · Image and prompt",
            "openai" or "google" or "xai" or "bfl" => "Scene guidance · Rhino context",
            _ => "Scene guidance"
        };
        if (guidance.Header is Label guidanceLabel && guidanceLabel.Text != guidanceTitle)
            guidanceLabel.Text = guidanceTitle;
        images.Update(prepared, session.RecentRenders, session.ProjectRenders);
        if (!images.Visible && !setup.Visible)
        {
            setup.Visible = true;
            toggleSetup.Text = "Hide setup";
        }
        toggleSetup.Visible = images.Visible;
        RefreshView();
    }

    private void RefreshView()
    {
        var document = RhinoDoc.FromRuntimeSerialNumber(serial);
        session.UpdateFrameOverlay();
        var text = SystemColors.ControlText;
        var brand = text.R + text.G + text.B > 1.5f ? darkSymbol : lightSymbol;
        if (symbol.Image != brand) symbol.Image = brand;
        bool custom = resolution.SelectedIndex == 3;
        bool linked = lockRatio.Checked != false;
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
                ? linked ? ReadCustomSize(pixels) : ReadFreeSize()
                : ResolutionPlanner.ScaleViewport(pixels, multiplier);
            if (!session.IsBusy)
            {
                SetCustomSize(size);
                session.CustomSize = custom ? size : null;
                session.Multiplier = multiplier;
            }
            bool framed = custom && !linked && !ResolutionPlanner.KeepsLinkedAspect(pixels, size);
            string sizeText = $"{size.Width:N0} × {size.Height:N0} px" + (framed ? $" · {RatioLabel(size)}" : "");
            string processing = "";
            string sizeHelp = framed
                ? "Saved image size. The centered frame in the view is captured at one uniform scale; the excluded edges stay out of your image."
                : "Saved image size. The complete view is kept at one uniform scale; small borders can result from whole-pixel rounding.";
            if (session.SelectedProvider == "local" && !session.CaptureTestSelected)
            {
                dimensions.Text = sizeText;
                sizeDetails.Visible = false;
                DisablePreview("Choose a model to prepare a preview.");
                return;
            }
            if (session.SelectedModel is not { } selectedModel)
            {
                dimensions.Text = sizeText;
                sizeDetails.Visible = false;
                DisablePreview(session.ModelUnavailableMessage);
                return;
            }
            if (!selectedModel.Capabilities.IsDevelopmentPassthrough)
            {
                var plan = selectedModel.Plan(ResolutionPlanner.CaptureSize(pixels, size), size);
                sizeHelp += $" Model canvas: {plan.Native.Width:N0} × {plan.Native.Height:N0} pixels.";
                if (session.SelectedProvider == "managed")
                    sizeHelp += " Local generation takes minutes for a large canvas; the status line follows each step while it works.";
                if (framed)
                {
                    var frame = SafeFrame.LargestCentered(pixels, size);
                    sizeHelp += $" Frame: the centered {frame.Width:N0} × {frame.Height:N0} px rectangle inside the view.";
                }
                if (plan.HasPadding)
                {
                    sizeHelp += " The surrounding padding is removed for export.";
                }
                if (plan.RequiresResampling)
                {
                    processing = $"Generated area · {plan.ContentRectangle.Width:N0} × {plan.ContentRectangle.Height:N0} px · resized for export";
                    sizeHelp += $" The model paints {plan.ContentRectangle.Width:N0} × {plan.ContentRectangle.Height:N0} content pixels before export scaling.";
                }
                if (session.SelectedProvider == "managed" || plan.Native.Pixels > 2560L * 1440)
                    sizeHelp += " This model's resolution support is experimental.";
                if (session.SelectedProvider == "openai" && plan.Native.Pixels > 2560L * 1440)
                    processing = processing.Length > 0 ? processing + "\nExperimental image size" : "Experimental image size";
            }
            dimensions.Text = sizeText;
            sizeDetails.Text = processing;
            sizeDetails.Visible = processing.Length > 0;
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
            sizeDetails.Visible = false;
            dimensions.Text = ex is OverflowException ? "Choose a smaller image size." : ex.Message;
            DisablePreview("Enter a supported size to preview the view.");
        }
        catch (InvalidOperationException ex)
        {
            viewLabel.Text = ex.Message;
            dimensions.Text = "";
            DisablePreview("Choose or show a supported view, then preview again.");
        }
        DrawRenderProgress();
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

    // Unlocked dimensions are typed freely; the pair's ratio drives the frame.
    private PixelSize ReadFreeSize()
    {
        if (!TryReadDimension(width.Text, out int freeWidth) || !TryReadDimension(height.Text, out int freeHeight))
            throw new ArgumentException("Enter a whole number from 1 to 16,384 pixels for width and height.");
        var size = new PixelSize(freeWidth, freeHeight);
        ResolutionPlanner.ValidateSize(size, ResolutionPlanner.ExportPixelLimit);
        return size;
    }

    private void UpdateLockTooltips(bool linked)
    {
        width.ToolTip = linked
            ? "Width in pixels. Height follows the view's proportions. Press Enter or leave the field to apply."
            : "Width in pixels, typed freely. The pair's ratio frames a centered safe frame in the view. Press Enter or leave the field to apply.";
        height.ToolTip = linked
            ? "Height in pixels. Width follows the view's proportions. Press Enter or leave the field to apply."
            : "Height in pixels, typed freely. The width stays exactly as typed. Press Enter or leave the field to apply.";
    }

    private static bool TryReadDimension(string text, out int value) =>
        int.TryParse(text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value) && value is >= 1 and <= 16384;

    // A plain reduced ratio, e.g. "4:3" or "23:9".
    internal static string RatioLabel(PixelSize size)
    {
        int a = size.Width, b = size.Height;
        while (b != 0)
            (a, b) = (b, a % b);
        return $"{size.Width / a}:{size.Height / a}";
    }

    private void ApplyCustomSize()
    {
        if (resolution.SelectedIndex != 3 || session.IsBusy || RhinoDoc.FromRuntimeSerialNumber(serial) is not { } document) return;
        try
        {
            if (lockRatio.Checked == false)
            {
                // Unlocked: both dimensions are the typed truth, no rounding.
                session.CustomSize = ReadFreeSize();
                return;
            }
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


}

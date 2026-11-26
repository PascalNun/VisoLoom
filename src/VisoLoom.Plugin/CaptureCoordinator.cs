using System.Text.Json;
using Rhino;
using VisoLoom.Core;
using VisoLoom.Protocol;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal sealed record CompletedRender(CapturedScene Capture, ProviderResult Output, ImageSaveReport Saving)
{
    public long Bytes => Capture.Png.LongLength + Output.PngBytes.LongLength;
}

internal sealed class CaptureCoordinator
{
    public const string ProjectModelKey = "project-model";
    private static readonly Dictionary<uint, CaptureCoordinator> Sessions = [];
    private static readonly RenderModelProfile CaptureTestModel = new("local", ReferenceCapabilities.Create().Models.Single());
    private static readonly RenderModelProfile OpenAiDefault = new("openai", OpenAiImageProvider.Model, OpenAiImageProvider.MaximumInputBytes);
    private static readonly RenderModelProfile GoogleDefault = new("google", GoogleImageProvider.AccountModel(GoogleImageProvider.ModelId), GoogleImageProvider.MaximumInputBytes);
    private static readonly RenderModelProfile XaiDefault = new("xai", XaiImageProvider.AccountModel(XaiImageProvider.ModelId), XaiImageProvider.MaximumInputBytes);
    private static readonly RenderModelProfile BflDefault = new("bfl", BflImageProvider.AccountModel(BflImageProvider.ModelId));
    private static bool jobInFlight;
    // The cloud providers share one prompt, consent and provenance flow; xAI
    // and BFL are wired exactly like OpenAI and Google (ADR 0026/0027).
    private static bool IsCloud(string providerId) => providerId is "openai" or "google" or "xai" or "bfl";
    private static string CloudName(string providerId) => providerId switch { "openai" => "OpenAI", "google" => "Google", "xai" => "xAI", "bfl" => "BFL", _ => "" };
    // The status line follows the local worker's own phases, so a full-canvas
    // edit on a small computer shows its real pace instead of one silent string.
    private static string LocalStatusLabel(string providerId, ProviderProgress progress)
    {
        if (providerId != "managed")
            return "Returning the unchanged reference…";
        switch (progress.Phase)
        {
            case "checkingLocalFiles":
            case "checkingPrompt":
            case "decodingReference":
                return "Preparing the local edit…";
            case "loadingModel":
                return "Loading the local model…";
            case "validatingOutput":
                return "Checking the generated image…";
            case "generating" when progress.Step is { } step && progress.Steps is { } steps:
                return $"Generating with the selected local model · step {step} of {steps}" +
                    (progress.EtaSeconds is { } eta ? $" · {WaitLabel(eta)} left" : "") + "…";
            default:
                return "Generating with the selected local model…";
        }
    }
    private static string WaitLabel(double seconds)
    {
        if (seconds >= 90)
            return $"about {Math.Max(2, (int)Math.Round(seconds / 60.0))} minutes";
        if (seconds >= 45)
            return "about a minute";
        if (seconds >= 15)
            return $"about {Math.Max(15, (int)Math.Round(seconds / 15.0) * 15)} seconds";
        return "moments";
    }
    private static bool IsCloudConnected(string providerId) => providerId switch
    {
        "openai" => ConnectionSettings.IsReady,
        "google" => ConnectionSettings.GoogleIsReady,
        "xai" => ConnectionSettings.XaiIsReady,
        "bfl" => ConnectionSettings.BflIsReady,
        _ => false
    };
    // The guidance status every cloud provider shares, named for its own connection check.
    private static string CloudStatus(string providerId) => IsCloudConnected(providerId)
        ? CloudName(providerId) + " is connected. Preview the view to continue."
        : "Connect " + CloudName(providerId) + " in Settings to render.";
    private readonly uint documentSerial;
    private readonly string sessionId = Guid.NewGuid().ToString("N");
    private string token = "";
    private CancellationTokenSource? cancellation;
    private bool closed;
    private readonly FrameOverlay overlay = new();
    private bool automaticConnection;
    private DateTime nextConnectionAttempt;
    private ResolutionPlan? savedPlan;
    private PreparedCapture? preview;
    private string? outputFolder;
    private bool previewing;
    private readonly List<CompletedRender> recentRenders = [];
    public bool CanReconnect { get; private set; }
    public Uri Endpoint { get; private set; } = new("http://127.0.0.1:5179/");
    public bool IsConnecting { get; private set; }
    public ModelCapabilities? LocalModel { get; private set; }
    // Every local model the connected engine validated; the menu lists each.
    public IReadOnlyList<ModelCapabilities> ManagedModels { get; private set; } = [];
    private string selectedModelKey = "local";
    public ProjectModelPin? ProjectPin { get; private set; }
    public IReadOnlyList<ProjectRenderRow> ProjectRenders { get; private set; } = [];
    // A pin restored from the document is the visible selection, but a cloud
    // destination still needs one explicit in-session Model-menu choice: a
    // received .3dm must not spend the reader's OpenAI account on a
    // destination its author chose.
    private bool projectPinChosenInSession;
    private string selectedOpenAiModelId = "";
    private string selectedGoogleModelId = "";
    private string selectedXaiModelId = "";
    private string selectedBflModelId = "";
    private string selectedManagedModelId = "";
    public bool UsingProjectPin => SelectedModelKey == ProjectModelKey;
    public string SelectedProvider => !UsingProjectPin ? SelectedModelKey : ProjectPin?.ProviderId switch
    {
        "local" => "managed",
        "openai" => "openai",
        "google" => "google",
        "xai" => "xai",
        "bfl" => "bfl",
        _ => ""
    };
    public string SelectedModelKey
    {
        get => selectedModelKey;
        set
        {
            if (selectedModelKey == value || IsBusy || closed ||
                value is not ("local" or "openai" or "google" or "xai" or "bfl" or "managed" or ProjectModelKey) ||
                value == ProjectModelKey && ProjectPin is null)
                return;
            selectedModelKey = value;
            if (value == ProjectModelKey)
                projectPinChosenInSession = true; // The Model-menu selection is the explicit endorsement.
            InvalidatePreview();
            if (SelectedModel is null)
                SetStatus(ModelUnavailableMessage);
            else if (IsCloud(SelectedProvider))
                SetStatus(CloudStatus(SelectedProvider), isGuidance: true);
            else if (SelectedProvider == "managed")
                SetStatus("Preview the view to render on this computer.", isGuidance: true);
            else
                SetStatus(token.Length != 0 ? "Capture test connected." : "Set up the capture test connection in Settings.", isGuidance: true);
        }
    }
    // The in-session OpenAI account model. Empty keeps the compiled tested
    // profile; an explicit Model-menu choice names one image model the account
    // listed. A project pin supplies its own id while it is selected.
    public string SelectedOpenAiModelId
    {
        get => selectedOpenAiModelId;
        set
        {
            if (closed || IsBusy)
                return;
            if (value == OpenAiImageProvider.ModelId)
                value = "";
            if (value.Length > 127 || value.Any(char.IsControl) ||
                value.Length != 0 && !OpenAiImageProvider.IsFamilyModel(value))
                return;
            // The compiled row is the provider's default id: choosing it while
            // another provider is selected still switches the provider — an
            // unchanged stored id means "already there" only when already
            // selected here.
            if (value == selectedOpenAiModelId && selectedModelKey == "openai" && !UsingProjectPin)
                return;
            selectedOpenAiModelId = value;
            InvalidatePreview();
            bool wasOpenAi = selectedModelKey == "openai" && !UsingProjectPin;
            SelectedModelKey = "openai";
            if (wasOpenAi)
                SetStatus(ConnectionSettings.IsReady ? "OpenAI is connected. Preview the view to continue." : "Connect OpenAI in Settings to render.", isGuidance: true);
        }
    }

    // The in-session Google account model: empty keeps the compiled tested
    // profile; an explicit Model-menu choice names one image model the account
    // listed. A project pin supplies its own id while it is selected.
    public string SelectedGoogleModelId
    {
        get => selectedGoogleModelId;
        set
        {
            if (closed || IsBusy)
                return;
            if (value == GoogleImageProvider.ModelId)
                value = "";
            if (value.Length > 127 || value.Any(char.IsControl) ||
                value.Length != 0 && !GoogleImageProvider.IsImageModel(value))
                return;
            // Same as the OpenAI selection: the compiled row switches here even
            // though it stores as the empty default id.
            if (value == selectedGoogleModelId && selectedModelKey == "google" && !UsingProjectPin)
                return;
            selectedGoogleModelId = value;
            InvalidatePreview();
            bool wasGoogle = selectedModelKey == "google" && !UsingProjectPin;
            SelectedModelKey = "google";
            if (wasGoogle)
                SetStatus(CloudStatus("google"), isGuidance: true);
        }
    }

    // The in-session xAI account model: empty keeps the compiled tested
    // profile; an explicit Model-menu choice names one image model the account
    // listed. A project pin supplies its own id while it is selected.
    public string SelectedXaiModelId
    {
        get => selectedXaiModelId;
        set
        {
            if (closed || IsBusy)
                return;
            if (value == XaiImageProvider.ModelId)
                value = "";
            if (value.Length > 127 || value.Any(char.IsControl) ||
                value.Length != 0 && !XaiImageProvider.IsImageModel(value))
                return;
            // Same as the OpenAI selection: the compiled row switches here even
            // though it stores as the empty default id.
            if (value == selectedXaiModelId && selectedModelKey == "xai" && !UsingProjectPin)
                return;
            selectedXaiModelId = value;
            InvalidatePreview();
            bool wasXai = selectedModelKey == "xai" && !UsingProjectPin;
            SelectedModelKey = "xai";
            if (wasXai)
                SetStatus(CloudStatus("xai"), isGuidance: true);
        }
    }

    // The in-session BFL model: BFL has no account list, so the documented
    // klein profile is the selectable set; empty keeps the default row.
    // A project pin supplies its own id while it is selected.
    public string SelectedBflModelId
    {
        get => selectedBflModelId;
        set
        {
            if (closed || IsBusy)
                return;
            if (value == BflImageProvider.ModelId)
                value = "";
            // Same as the OpenAI selection: the default row switches here even
            // though it stores as the empty default id.
            if ((value == selectedBflModelId || value != BflImageProvider.PreviewModelId) &&
                selectedModelKey == "bfl" && !UsingProjectPin)
                return;
            selectedBflModelId = value;
            InvalidatePreview();
            bool wasBfl = selectedModelKey == "bfl" && !UsingProjectPin;
            SelectedModelKey = "bfl";
            if (wasBfl)
                SetStatus(CloudStatus("bfl"), isGuidance: true);
        }
    }

    // The in-session local model: empty keeps the engine's first listed row;
    // an explicit Model-menu choice names one installed model the engine
    // validated. A project pin supplies its own id while it is selected.
    public string SelectedManagedModelId
    {
        get => selectedManagedModelId;
        set
        {
            if (closed || IsBusy)
                return;
            if (value.Length > 127 || value.Any(char.IsControl) ||
                value.Length != 0 && ManagedModels.All(model => model.Id != value))
                return;
            // The first listed row switches the provider even though it stores
            // as the empty default id, exactly like the compiled cloud rows.
            if (value == selectedManagedModelId && selectedModelKey == "managed" && !UsingProjectPin)
                return;
            selectedManagedModelId = value;
            LocalModel = SelectedManagedModel(value);
            InvalidatePreview();
            bool wasManaged = selectedModelKey == "managed" && !UsingProjectPin;
            SelectedModelKey = "managed";
            if (wasManaged)
                SetStatus("Preview the view to render on this computer.", isGuidance: true);
        }
    }

    private ModelCapabilities? SelectedManagedModel(string id) =>
        ManagedModels.FirstOrDefault(model => model.Id == id) ?? ManagedModels.FirstOrDefault();

    public RenderModelProfile? SelectedModel
    {
        get
        {
            RenderModelProfile? model = SelectedProvider switch
            {
                "local" when !UsingProjectPin => CaptureTestModel,
                "openai" => OpenAiSelection(),
                "google" => GoogleSelection(),
                "xai" => XaiSelection(),
                "bfl" => BflSelection(),
                "managed" when LocalModel is { } local &&
                    VisoLoom.Protocol.ManagedModels.Find(local.Id) is { } definition => new("managed", local, definition.MaximumImageBytes),
                _ => null
            };
            return UsingProjectPin && model?.Identity != ProjectPin ? null : model;
        }
    }

    // Any account image model resolves to its profile: the compiled tested
    // profile for the reviewed snapshot, the family baseline for later ones.
    private RenderModelProfile? OpenAiSelection()
    {
        string id = UsingProjectPin ? ProjectPin?.ModelId ?? "" : selectedOpenAiModelId;
        return id.Length == 0
            ? OpenAiDefault
            : OpenAiImageProvider.IsFamilyModel(id)
                ? new("openai", OpenAiImageProvider.AccountModel(id), OpenAiImageProvider.MaximumInputBytes)
                : null;
    }

    private RenderModelProfile? GoogleSelection()
    {
        string id = UsingProjectPin ? ProjectPin?.ModelId ?? "" : selectedGoogleModelId;
        return id.Length == 0
            ? GoogleDefault
            : GoogleImageProvider.IsImageModel(id)
                ? new("google", GoogleImageProvider.AccountModel(id), GoogleImageProvider.MaximumInputBytes)
                : null;
    }

    private RenderModelProfile? XaiSelection()
    {
        string id = UsingProjectPin ? ProjectPin?.ModelId ?? "" : selectedXaiModelId;
        return id.Length == 0
            ? XaiDefault
            : XaiImageProvider.IsImageModel(id)
                ? new("xai", XaiImageProvider.AccountModel(id), XaiImageProvider.MaximumInputBytes)
                : null;
    }

    private RenderModelProfile? BflSelection()
    {
        string id = UsingProjectPin ? ProjectPin?.ModelId ?? "" : selectedBflModelId;
        return id.Length == 0
            ? BflDefault
            : BflImageProvider.IsCuratedModel(id)
                ? new("bfl", BflImageProvider.AccountModel(id))
                : null;
    }
    public string ModelUnavailableMessage => UsingProjectPin
        ? ProjectPin is { } pin && CompatibilityCatalogueStore.Current?.Find(pin.ProviderId, pin.ModelId) is { Retired: true }
            ? "This project's saved model is retired. Choose another model in the Model menu."
            : "This project's saved model is unavailable. Reconnect it in Settings or choose another model."
        : "No local model is connected. Check the connection in Settings.";
    // The representation default is a per-user preference: the last chosen
    // preset id persists in Rhino settings, restored when it still exists.
    public RepresentationPreset SelectedPreset { get; set; } = PromptPresets.DefaultChoice();
    public string UserPrompt { get; set; } = "";
    // The run's sampling dial: a per-render override of the reviewed recipe,
    // null meaning the recipe itself. It freezes into the preview's intent.
    public int? SamplingSteps { get; set; }
    // The live progress record of a running render, for the panel's bar,
    // preview image and clock; null whenever nothing renders.
    public ProviderProgress? LiveProgress { get; private set; }
    // When the running render started — the cloud clock's honest source,
    // because a synchronous cloud call reports no percentage of its own.
    public DateTime? RenderStarted { get; private set; }
    public RenderIntent? Intent { get; private set; }
    public string? ActiveProvider { get; private set; }
    public bool IsConfigured => SelectedModel is not null && SelectedProvider switch
    {
        "openai" => ConnectionSettings.IsReady,
        "google" => ConnectionSettings.GoogleIsReady,
        "xai" => ConnectionSettings.XaiIsReady,
        "bfl" => ConnectionSettings.BflIsReady,
        "local" or "managed" => token.Length != 0,
        _ => false
    };
    public bool IsBusy => previewing || cancellation is not null;
    public byte[]? PreviewPng => preview?.PreparedPng;
    public ResolutionPlan? PreviewPlan => preview?.Plan;
    public string? PreviewModelName => preview?.Choice.Model.Capabilities.DisplayName;
    public bool PreviewSubmitted => preview?.Guard.IsSubmitted == true;
    public int Multiplier { get; set; } = 1;
    public PixelSize? CustomSize { get; set; }
    public bool LinkedRatio { get; set; } = true;
    // Null follows the active view; an ID pins an open model viewport without activating it.
    public Guid? SourceViewportId { get; set; }
    public NamedViewSource NamedView { get; } = new();
    public string Status { get; private set; } = "Choose a model in Settings to get started.";
    public bool StatusIsGuidance { get; private set; } = true;
    public string? HistoryDirectory { get; private set; }
    public CapturedScene? Capture { get; private set; }
    public ProviderResult? Result { get; private set; }
    public ImageSaveReport? ImageSave { get; private set; }
    public IReadOnlyList<CompletedRender> RecentRenders => recentRenders;

    public event EventHandler? Changed;
    static CaptureCoordinator()
    {
        RhinoDoc.CloseDocument += (_, e) =>
        {
            if (Sessions.Remove(e.Document.RuntimeSerialNumber, out var session))
            {
                session.closed = true;
                session.token = "";
                session.preview = null;
                session.Capture = null;
                session.Result = null;
                session.ImageSave = null;
                session.recentRenders.Clear();
                session.Changed = null;
            }
        };
    }

    private CaptureCoordinator(uint serial)
    {
        documentSerial = serial;
        var document = RhinoDoc.FromRuntimeSerialNumber(serial);
        ProjectPin = ProjectModelPin.Parse(document?.Strings.GetValue(ProjectModelPin.DocumentKey));
        ProjectRenders = ProjectRenderIndex.Parse(document?.Strings.GetValue(ProjectRenderIndex.DocumentKey));
        if (ProjectPin is not null)
            // The restored pin is visible immediately; a cloud destination
            // renders only after the user's explicit choice below.
            selectedModelKey = ProjectModelKey;
        ShowRecommendationChange();
    }

    // A recommendation is guidance before the next preview, never a silent
    // switch: this Rhino session remembers, per provider, the recommendation it
    // last showed and says when a newer compiled catalogue changes it.
    private void ShowRecommendationChange()
    {
        var catalogue = CompatibilityCatalogueStore.Current;
        if (catalogue is null || RecommendationPolicy.Recommended(catalogue, SelectedProvider) is not { } recommended)
            return;
        string key = "ModelRecommendation.Seen." + SelectedProvider;
        // The fixture assembly has no registered plug-in; its settings getter
        // fails loudly if reached there.
        string seen = VisoLoomPlugIn.Instance!.Settings.GetString(key, "");
        if (RecommendationPolicy.ChangeNotice(catalogue, SelectedProvider,
                string.IsNullOrEmpty(seen) ? null : seen, SelectedModel?.Capabilities.Id) is { } notice)
            SetStatus(notice, isGuidance: true);
        VisoLoomPlugIn.Instance!.Settings.SetString(key, recommended.ModelId);
    }

    public void SetProjectPin(RhinoDoc doc)
    {
        if (IsBusy || closed || doc.RuntimeSerialNumber != documentSerial || doc.IsReadOnly ||
            SelectedModel is not { Capabilities.IsDevelopmentPassthrough: false } model)
            return;
        var pin = model.Identity;
        if (pin == ProjectPin)
        {
            SelectedModelKey = ProjectModelKey;
            return;
        }
        string value = JsonSerializer.Serialize(pin, WireJson.Options);
        if (ProjectModelPin.Parse(value) is null)
        {
            SetStatus("This model does not provide a version that can be saved in the Rhino file.");
            return;
        }
        doc.Strings.SetString(ProjectModelPin.DocumentKey, value);
        if (doc.Strings.GetValue(ProjectModelPin.DocumentKey) != value)
        {
            SetStatus("Rhino could not save the project model choice.");
            return;
        }
        ProjectPin = pin;
        doc.Modified = true;
        projectPinChosenInSession = true; // Saving the pin is itself an explicit choice.
        selectedModelKey = ProjectModelKey;
        InvalidatePreview();
        SetStatus("Model choice saved to this project. Save the Rhino file to keep it.");
    }

    public void ClearProjectPin(RhinoDoc doc)
    {
        if (IsBusy || closed || doc.RuntimeSerialNumber != documentSerial || doc.IsReadOnly || ProjectPin is null)
            return;
        doc.Strings.Delete(ProjectModelPin.DocumentKey);
        if (!string.IsNullOrEmpty(doc.Strings.GetValue(ProjectModelPin.DocumentKey)))
        {
            SetStatus("Rhino could not remove the project model choice.");
            return;
        }
        if (UsingProjectPin)
            selectedModelKey = SelectedProvider is "openai" or "google" or "xai" or "bfl" or "managed" ? SelectedProvider : "local";
        ProjectPin = null;
        projectPinChosenInSession = false;
        doc.Modified = true;
        InvalidatePreview();
        SetStatus("Saved model choice removed. Save the Rhino file to keep this change.");
    }
    public Rhino.Display.RhinoView SourceView(RhinoDoc doc) => NamedView.Name is null
        ? SceneBuilder.FindView(doc, SourceViewportId)
        : NamedView.Resolve(doc);

    public void ShowNamedView(RhinoDoc doc)
    {
        if (IsBusy || closed || doc.RuntimeSerialNumber != documentSerial)
            return;
        try
        {
            InvalidatePreview();
            NamedView.Show(doc);
            SetStatus("Named view shown. Preview the visible frame to continue.", isGuidance: true);
        }
        catch (InvalidOperationException ex) { SetStatus(ex.Message); }
    }

    public void InvalidatePreview() => preview?.Guard.Invalidate();

    public bool PreviewIsCurrent(RhinoDoc doc)
    {
        if (preview is null || closed || doc.RuntimeSerialNumber != documentSerial)
            return false;
        try { return preview.Guard.IsCurrent(PreviewFingerprint(doc)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            preview.Guard.Invalidate();
            return false;
        }
    }

    private PixelSize RequestedSize(Rhino.Display.RhinoView view)
    {
        var viewport = SceneBuilder.ViewportSize(view);
        var size = CustomSize ?? ResolutionPlanner.ScaleViewport(viewport, Multiplier);
        ResolutionPlanner.ValidateSize(size, ResolutionPlanner.ExportPixelLimit);
        return size;
    }

    // ADR 0030: the frame conduit exists only while the panel holds a lock-off
    // custom size. Locked, viewport-multiple and unavailable states turn it
    // off, and the capture path suspends it around the capture call.
    public void UpdateFrameOverlay()
    {
        if (closed)
        {
            overlay.TurnOff();
            return;
        }
        Rhino.Display.RhinoView? view = null;
        RhinoDoc? document = null;
        try
        {
            document = RhinoDoc.FromRuntimeSerialNumber(documentSerial);
            view = document is null ? null : SourceView(document);
        }
        catch (InvalidOperationException) { /* RefreshView explains unavailable sources. */ }
        var chosen = view is not null && !LinkedRatio && CustomSize is { } size ? size : (PixelSize?)null;
        // Redraw only on a real overlay change. A redraw per timer tick while
        // the frame was shown made Rhino recompute the camera frustum with
        // rounding noise, which expired a fresh preview within a second.
        if (overlay.Update(view?.ActiveViewportID ?? Guid.Empty, chosen))
            view?.Redraw();
    }

    internal bool FrameOverlaySuspended => overlay.IsSuspended;

    private RenderChoice CurrentChoice()
    {
        var model = SelectedModel ?? throw new InvalidOperationException(ModelUnavailableMessage);
        bool cloud = IsCloud(model.ProviderId);
        var destination = cloud
            ? model.ProviderId switch
            {
                "google" => GoogleImageProvider.Origin,
                "xai" => XaiImageProvider.Origin,
                "bfl" => BflImageProvider.Origin,
                _ => OpenAiImageProvider.Origin
            }
            : Endpoint;
        return new(model, destination, UsingProjectPin, SelectedPreset, UserPrompt, cloud && ConnectionSettings.IncludeNames, SamplingSteps);
    }

    private string PreviewFingerprint(RhinoDoc doc)
    {
        var view = SourceView(doc);
        return JsonSerializer.Serialize(new
        {
            SceneBuilder.Revision,
            ConnectionRevision = ConnectionSettings.Revision,
            NamedView.Name,
            SourceViewportId,
            Signature = SceneBuilder.ViewSignature(view),
            Size = RequestedSize(view),
            Choice = CurrentChoice()
        }, WireJson.Options);
    }

    // UI-thread only. No credentials, connection discovery, history writes or provider calls.
    public void Preview(RhinoDoc doc)
    {
        if (IsBusy || jobInFlight || closed || doc.RuntimeSerialNumber != documentSerial)
            return;
        InvalidatePreview();
        previewing = true;
        SetStatus("Capturing the Rhino view…");
        try
        {
            string fingerprint = PreviewFingerprint(doc);
            var view = SourceView(doc);
            var size = RequestedSize(view);
            var choice = CurrentChoice();
            var captureSize = ResolutionPlanner.CaptureSize(SceneBuilder.ViewportSize(view), size);
            var plan = choice.Model.Plan(captureSize, size);
            CapturedScene captured;
            // ADR 0030: the frame conduit is disabled around the capture call,
            // so the overlay never reaches a saved source image.
            overlay.Suspend();
            try
            {
                captured = SceneBuilder.Capture(doc, view.ActiveViewportID, captureSize, sessionId);
            }
            finally { overlay.Resume(); }
            if (NamedView.Name is { } name)
                captured = captured with { ViewName = name };
            var intent = IsCloud(choice.ProviderId)
                ? ScenePromptComposer.Compose(captured.Manifest, choice.Preset, choice.UserPrompt, plan, choice.IncludeNames)
                : VisoLoom.Protocol.ManagedModels.Find(choice.Model.Capabilities.Id) is { } definition ? definition.Compose(choice.Preset, choice.UserPrompt, size, choice.SamplingSteps)
                : new RenderIntent { RequestedOutput = size };
            byte[] prepared = choice.ProviderId != "local"
                ? new RhinoImageProcessor().PrepareReference(captured.Png, plan) : captured.Png;
            if (PngHeader.Read(prepared) != plan.Native || prepared.LongLength > choice.Model.MaximumInputBytes)
                throw new ArgumentException("The prepared image exceeds the model's input limits. Choose a smaller size.");
            if (fingerprint != PreviewFingerprint(doc))
                throw new InvalidOperationException("The scene or view changed during preview. Preview the capture again.");
            preview = new(captured, prepared, plan, intent, choice, fingerprint, new(fingerprint));
            Capture = captured;
            Intent = intent;
            Result = null;
            ImageSave = null;
            HistoryDirectory = null;
            CanReconnect = false;
            SetStatus("Source image ready. Review it before rendering.", isGuidance: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { SetStatus(ex.Message); }
        catch (OverflowException) { SetStatus("The requested capture dimensions are too large."); }
        catch (Exception) { SetStatus("Rhino could not prepare the preview. Choose a smaller size or another native display style."); }
        finally
        {
            previewing = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    public static CaptureCoordinator ForDocument(uint serial)
    {
        if (!Sessions.TryGetValue(serial, out var session))
            Sessions.Add(serial, session = new(serial));
        return session;
    }

    public void Configure(Uri endpoint, string sessionToken)
    {
        if (IsBusy || IsConnecting)
            throw new InvalidOperationException("Wait for the current connection check or capture test to finish.");
        EndpointPolicy.RequireLocalEndpoint(endpoint);
        if (sessionToken.Length < 32 || sessionToken.Any(char.IsControl))
            throw new ArgumentException("Enter the server's session token (at least 32 characters).");
        if (CanReconnect && endpoint != Endpoint)
            throw new ArgumentException("Reconnect uses the original local server. Start a new capture test to change destinations.");
        InvalidatePreview();
        Endpoint = endpoint;
        token = sessionToken;
        LocalModel = null;
        ManagedModels = [];
        automaticConnection = false;
        SetStatus("Local connection saved for this session.", isGuidance: true);
    }

    // Called on the UI thread; discovery never captures or uploads a Rhino scene.
    public async Task TryConnectAutomaticallyAsync()
    {
        if (closed || SelectedProvider is not ("local" or "managed") || token.Length != 0 || IsBusy || IsConnecting || DateTime.UtcNow < nextConnectionAttempt)
            return;
        nextConnectionAttempt = DateTime.UtcNow.AddSeconds(5);
        IsConnecting = true;
        Changed?.Invoke(this, EventArgs.Empty);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            var connection = await LocalDevelopmentConnection.FindAsync(deadline.Token).ConfigureAwait(false);
            if (connection is null)
                return;
            using var provider = new LocalProviderClient(connection.Endpoint, connection.Token);
            var capabilities = await provider.DiscoverAsync(deadline.Token).ConfigureAwait(false);
            await OnUi(() =>
            {
                if (!closed && token.Length == 0)
                {
                    Endpoint = connection.Endpoint;
                    token = connection.Token;
                    automaticConnection = true;
                    ManagedModels = LocalProviderClient.ActiveManagedModels(capabilities);
                    LocalModel = SelectedManagedModel(selectedManagedModelId);
                    if (SelectedProvider == "local")
                        SetStatus("Capture test connected. Preview the view to continue.", isGuidance: true);
                }
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ProviderException or IOException or ArgumentException)
        {
            // The launcher may still be starting. Retry without interrupting Rhino work.
        }
        finally
        {
            await OnUi(() =>
            {
                IsConnecting = false;
                if (!closed)
                    Changed?.Invoke(this, EventArgs.Empty);
                return true;
            }).ConfigureAwait(false);
        }
    }

    public async Task<ModelCapabilities> CheckLocalEngineAsync()
    {
        if (IsBusy || IsConnecting || closed)
            throw new InvalidOperationException("Wait for the current connection check or render to finish.");
        IsConnecting = true;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var connection = await LocalDevelopmentConnection.FindAsync(deadline.Token)
                ?? throw new InvalidOperationException("The local service is not running. See Connections in Settings.");
            using var provider = new LocalProviderClient(connection.Endpoint, connection.Token);
            var capabilities = await provider.DiscoverAsync(deadline.Token);
            ManagedModels = LocalProviderClient.ActiveManagedModels(capabilities);
            var model = SelectedManagedModel(selectedManagedModelId)
                ?? throw new InvalidOperationException("No reviewed local model is ready.");
            if (closed || IsBusy)
                throw new InvalidOperationException("The document closed or started rendering during the connection check.");
            InvalidatePreview();
            LocalModel = model;
            Endpoint = connection.Endpoint;
            token = connection.Token;
            automaticConnection = true;
            return model;
        }
        catch
        {
            if (!closed && !IsBusy)
            {
                InvalidatePreview();
                LocalModel = null;
                ManagedModels = [];
            }
            throw;
        }
        finally
        {
            IsConnecting = false;
            if (!closed)
                Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RefreshAutomaticConnectionAsync(bool reconnect, CancellationToken ct)
    {
        if (!automaticConnection)
            return;
        var connection = await LocalDevelopmentConnection.FindAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The local service is unavailable. See Local setup in Settings.");
        await OnUi(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (closed)
                throw new InvalidOperationException("The source document closed before connection.");
            if (reconnect && connection.Endpoint != Endpoint)
                throw new InvalidOperationException("The local destination changed. Start a new capture test instead of reconnecting the saved job.");
            Endpoint = connection.Endpoint;
            token = connection.Token;
            return true;
        }).ConfigureAwait(false);
    }

    public void Start(RhinoDoc doc, RepresentationPreset? preset = null, string? userPrompt = null)
    {
        if (IsBusy)
            return;
        if (IsConnecting)
        {
            SetStatus("Checking the local connection. Please wait…");
            return;
        }
        if (jobInFlight)
        {
            SetStatus("VisoLoom is already working on an image. Wait for it to finish or cancel it first.");
            return;
        }

        if (!IsConfigured)
        {
            SetStatus(SelectedModel is null ? ModelUnavailableMessage : "Set up the selected model connection in Settings to render.");
            return;
        }

        if (doc.RuntimeSerialNumber != documentSerial || closed)
            return;
        SelectedPreset = preset ?? SelectedPreset;
        UserPrompt = userPrompt ?? UserPrompt;
        if (!PreviewIsCurrent(doc))
        {
            SetStatus("Update the source image before rendering.");
            return;
        }
        if (UsingProjectPin && !projectPinChosenInSession && IsCloud(SelectedProvider))
        {
            // A document-supplied cloud pin is disclosed, not assumed: the first
            // render with it needs its explicit Model-menu choice in this session.
            SetStatus($"This project's saved model is {SelectedModel?.Capabilities.DisplayName}. Choose it in the Model menu before rendering with it.");
            return;
        }
        var prepared = preview!;
        prepared.Guard.Submit(PreviewFingerprint(doc));
        var choice = prepared.Choice;
        string key = choice.ProviderId switch
        {
            "openai" => ConnectionSettings.ApiKey,
            "google" => ConnectionSettings.GoogleApiKey,
            "xai" => ConnectionSettings.XaiApiKey,
            "bfl" => ConnectionSettings.BflApiKey,
            _ => ""
        };
        // Connecting stores the key; consent travels with the connection.
        bool cloudAllowed = key.Length != 0;
        ActiveProvider = choice.ProviderId;
        jobInFlight = true;
        cancellation = new();
        Result = null;
        ImageSave = null;
        HistoryDirectory = null;
        CanReconnect = false;
        SetStatus(IsCloud(choice.ProviderId)
            ? "Checking " + CloudName(choice.ProviderId) + " image editing…"
            : "Checking local server…");
        outputFolder = ImageSavingSettings.OutputFolder;
        _ = RunAsync(prepared, key, cloudAllowed, outputFolder, cancellation.Token);
    }

    private async Task RunAsync(PreparedCapture prepared, string key, bool cloudAllowed, string? selectedOutputFolder, CancellationToken ct)
    {
        var choice = prepared.Choice;
        var captured = prepared.Capture;
        var plan = prepared.Plan;
        var intent = prepared.Intent;
        try
        {
            // Cloud jobs go to their own provider origin; only local and managed
            // jobs need this computer's engine connection to be where the
            // preview planned it. A cloud destination is never the loopback
            // endpoint, and no local refresh belongs before a cloud submission.
            if (!IsCloud(choice.ProviderId))
            {
                await RefreshAutomaticConnectionAsync(reconnect: false, ct).ConfigureAwait(false);
                if (Endpoint != choice.Destination)
                    throw new InvalidOperationException("The local destination changed. Preview again before rendering.");
            }
            using IRenderProvider provider = choice.ProviderId switch
            {
                "openai" => new OpenAiImageProvider(key, cloudAllowed, new RhinoImageProcessor(), modelId: choice.Model.Capabilities.Id),
                "google" => new GoogleImageProvider(key, cloudAllowed, new RhinoImageProcessor(), modelId: choice.Model.Capabilities.Id),
                "xai" => new XaiImageProvider(key, cloudAllowed, new RhinoImageProcessor(), modelId: choice.Model.Capabilities.Id),
                "bfl" => new BflImageProvider(key, cloudAllowed, new RhinoImageProcessor(), modelId: choice.Model.Capabilities.Id),
                _ => new LocalProviderClient(choice.Destination, token, modelId: choice.Model.Capabilities.Id,
                    images: new RhinoImageProcessor())
            };
            var caps = await provider.DiscoverAsync(ct).ConfigureAwait(false);
            var available = caps.Models.SingleOrDefault(model => model.Id == choice.Model.Capabilities.Id && model.Version == choice.Model.Capabilities.Version)
                ?? throw new InvalidOperationException("The selected model changed or is unavailable. Check the connection in Settings and preview again.");
            if (plan != new RenderModelProfile(choice.ProviderId, available).Plan(captured.Manifest.Frame, intent.RequestedOutput))
                throw new InvalidOperationException("The model's image limits changed. Preview again before rendering.");
            await OnUi(() =>
            {
                ct.ThrowIfCancellationRequested();
                var document = closed ? null : RhinoDoc.FromRuntimeSerialNumber(documentSerial);
                if (document is null)
                    throw new InvalidOperationException("The source document closed before submission.");
                if (PreviewFingerprint(document) != prepared.Fingerprint)
                    throw new InvalidOperationException("The scene or settings changed while connecting. Preview again before rendering. No image was sent.");
                return true;
            }).ConfigureAwait(false);
            savedPlan = plan;
            string directory = await Task.Run(() => LocalHistory.SaveCapture(captured, intent, plan, choice.Model.Identity,
                choice.ProjectPinned, prepared.PreparedPng), ct).ConfigureAwait(false);
            await OnUi(() =>
            {
                ct.ThrowIfCancellationRequested();
                var document = closed ? null : RhinoDoc.FromRuntimeSerialNumber(documentSerial);
                if (document is null || PreviewFingerprint(document) != prepared.Fingerprint)
                    throw new InvalidOperationException("The scene or settings changed before submission. Preview again. No image was sent.");
                Capture = captured;
                Intent = intent;
                HistoryDirectory = directory;
                RenderStarted = DateTime.UtcNow;
                LiveProgress = null;
                SetStatus(IsCloud(choice.ProviderId)
                    ? "Sending the captured view and selected context to " + CloudName(choice.ProviderId) + "…"
                    : "Uploading reference to this computer…");
                return true;
            }).ConfigureAwait(false);
            // Every progress report publishes the live record beside its
            // sentence, so the panel's bar, preview and clock draw from the
            // same report instead of re-parsing text.
            var progress = new CallbackProgress(p => RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                LiveProgress = p;
                SetStatus(p.Stage switch
            {
                "queued" => IsCloud(choice.ProviderId) ? "Queued at " + CloudName(choice.ProviderId) + "…" : "Queued on the local server…",
                "rendering" => IsCloud(choice.ProviderId) ? "Rendering…" : LocalStatusLabel(choice.ProviderId, p),
                "downloading" => "Checking returned PNG…",
                "generating" => IsCloud(choice.ProviderId) ? "Generating with " + CloudName(choice.ProviderId) + "…" : "Generating…",
                "preparing" => IsCloud(choice.ProviderId) ? "Checking the prepared reference for " + CloudName(choice.ProviderId) + "…" : "Checking the prepared reference…",
                "processing" => "Restoring the complete Rhino frame…",
                _ => "Uploading reference to this computer…"
                });
                Changed?.Invoke(this, EventArgs.Empty);
            })));
            var assets = new Dictionary<string, byte[]> { ["reference.png"] = captured.Png };
            var result = provider is OpenAiImageProvider openAi
                ? await openAi.RenderPreparedAsync(captured.Manifest, assets, intent, plan, prepared.PreparedPng, progress, ct).ConfigureAwait(false)
                : provider is GoogleImageProvider google
                ? await google.RenderPreparedAsync(captured.Manifest, assets, intent, plan, prepared.PreparedPng, progress, ct).ConfigureAwait(false)
                : provider is XaiImageProvider xai
                ? await xai.RenderPreparedAsync(captured.Manifest, assets, intent, plan, prepared.PreparedPng, progress, ct).ConfigureAwait(false)
                : provider is BflImageProvider bfl
                ? await bfl.RenderPreparedAsync(captured.Manifest, assets, intent, plan, prepared.PreparedPng, progress, ct).ConfigureAwait(false)
                : choice.ProviderId == "managed" && provider is LocalProviderClient local
                ? await local.RenderPreparedAsync(captured.Manifest, assets, intent, plan, prepared.PreparedPng, progress, ct).ConfigureAwait(false)
                : await provider.RenderAsync(captured.Manifest, assets, intent, plan, progress, ct).ConfigureAwait(false);
            var saved = await Task.Run(() => SaveCompletedImage(directory, result, selectedOutputFolder), CancellationToken.None).ConfigureAwait(false);
            await OnUi(() =>
            {
                RememberResult(captured, result, saved);
                SetStatus((result.Metadata.IsUnchangedReference ? result.Metadata.Plan.IsExact ? "Capture test complete. The image is unchanged." : "Capture test complete. The full view was resized for export." : "Render complete.") +
                    (saved.Problems.Count == 0 ? "" : saved.SavedPath is null
                        ? " Save the image before closing Rhino." : " Check the save warning below."));
                return true;
            }).ConfigureAwait(false);
        }
        catch (ProviderException ex)
        {
            // The refusal is recorded locally first; the panel sees only the
            // exception's own honest sentence.
            ProviderDiagnosticsLog.Record(choice.ProviderId, choice.Model.Capabilities.Id, ex);
            await OnUi(() =>
            {
                CanReconnect = choice.ProviderId == "local" && ex.Code is ("connectionLost" or "cancelUnconfirmed");
                SetStatus(ex.Message + (ex.JobId is { } id ? $" Job: {id:D}" : "") + FallbackOffer(choice.ProviderId));
                return true;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await OnUi(() =>
            {
                SetStatus("Stopped before submission.");
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            await OnUi(() =>
            {
                SetStatus(ex.Message);
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await OnUi(() =>
            {
                SetStatus("The operation failed. Check the connection, image support and available disk space. Accepted generation is never retried automatically.");
                return true;
            }).ConfigureAwait(false);
        }
        finally
        {
            await OnUi(() =>
            {
                cancellation?.Dispose();
                cancellation = null;
                ActiveProvider = null;
                LiveProgress = null;
                RenderStarted = null;
                jobInFlight = false;
                Changed?.Invoke(this, EventArgs.Empty);
                return true;
            }).ConfigureAwait(false);
        }
    }

    // A cloud that refused is answered with the local option already verified
    // on this computer: the offer names it and the Model menu keeps the one
    // deliberate step — nothing switches providers or spends on its own.
    private string FallbackOffer(string providerId) =>
        IsCloud(providerId) && LocalModel is { } local
            ? $" To render this view on this computer instead, choose {local.DisplayName} in the Model menu."
            : "";

    public void Reconnect()
    {
        if (jobInFlight || !CanReconnect || Capture is null || savedPlan is null || HistoryDirectory is null)
            return;
        ActiveProvider = "local";
        jobInFlight = true;
        cancellation = new();
        CanReconnect = false;
        SetStatus("Reconnecting to the captured job…");
        _ = ReconnectAsync(Capture, savedPlan, HistoryDirectory, cancellation.Token);
    }

    private async Task ReconnectAsync(CapturedScene captured, ResolutionPlan plan, string directory, CancellationToken ct)
    {
        try
        {
            await RefreshAutomaticConnectionAsync(reconnect: true, ct).ConfigureAwait(false);
            using var provider = new LocalProviderClient(Endpoint, token, images: new RhinoImageProcessor());
            // RenderAsync uses the snapshot UUID and identical request bytes; changed capabilities conflict safely.
            var result = await provider.RenderAsync(captured.Manifest, new Dictionary<string, byte[]> { ["reference.png"] = captured.Png }, new RenderIntent { RequestedOutput = plan.Final }, plan, cancellationToken: ct).ConfigureAwait(false);
            var saved = await Task.Run(() => SaveCompletedImage(directory, result, outputFolder)).ConfigureAwait(false);
            await OnUi(() =>
            {
                RememberResult(captured, result, saved);
                SetStatus((plan.IsExact ? "Capture test complete. The image is unchanged. " : "Capture test complete. The full view was resized for export. ") +
                    (saved.Problems.Count > 0 ? string.Join(" ", saved.Problems) + (saved.SavedPath is null ? " Use Save PNG… before closing Rhino." : "") : "Image saved automatically."));
                return true;
            }).ConfigureAwait(false);
        }
        catch (ProviderException ex)
        {
            await OnUi(() =>
            {
                CanReconnect = ex.Code is "connectionLost" or "cancelUnconfirmed";
                SetStatus(ex.Message);
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await OnUi(() =>
            {
                SetStatus("Reconnection failed. The saved capture remains in local history.");
                return true;
            }).ConfigureAwait(false);
        }
        finally
        {
            await OnUi(() =>
            {
                cancellation?.Dispose();
                cancellation = null;
                ActiveProvider = null;
                LiveProgress = null;
                RenderStarted = null;
                jobInFlight = false;
                Changed?.Invoke(this, EventArgs.Empty);
                return true;
            }).ConfigureAwait(false);
        }
    }

    public void Cancel()
    {
        if (cancellation is null)
            return;
        cancellation?.Cancel();
        SetStatus(IsCloud(ActiveProvider ?? "")
            ? "Stopping local waiting. " + CloudName(ActiveProvider!) + " may still finish and charge this request."
            : "Stopping; awaiting the server's cancellation state…");
    }

    internal void RememberResult(CapturedScene captured, ProviderResult result, ImageSaveReport saved)
    {
        if (closed) return;
        Result = result;
        ImageSave = saved;
        // Hold only a few comparison pairs for this Rhino document session.
        // Native canvas bytes are already written separately to private history.
        recentRenders.RemoveAll(item => item.Output.Metadata.ResultId == result.Metadata.ResultId);
        recentRenders.Insert(0, new(captured, result with { NativePngBytes = null }, saved));
        while (recentRenders.Count > 1 && (recentRenders.Count > 3 || recentRenders.Sum(item => item.Bytes) > 128L * 1024 * 1024))
            recentRenders.RemoveAt(recentRenders.Count - 1);
        // The project's render index keeps this render visible the next time
        // the file opens: the pixels stay on disk, the Rhino file carries the
        // row. A render without a saved file has nothing to reference.
        if (saved.SavedPath is { Length: > 0 } path && result.Metadata.Plan.Final is { Width: > 0, Height: > 0 } frame)
            ProjectRenders = ProjectRenderIndex.Append(RhinoDoc.FromRuntimeSerialNumber(documentSerial),
                new(result.Metadata.ResultId, path, ModelDisplay(result.Metadata), captured.ViewName, captured.DisplayStyle,
                    frame.Width, frame.Height, result.Metadata.CompletedAt, result.Metadata.IsUnchangedReference),
                ProjectRenders);
    }

    private static string ModelDisplay(RenderResult metadata) => metadata.ProviderId switch
    {
        "openai" => "OpenAI · " + metadata.ModelId,
        "google" => "Google · " + metadata.ModelId,
        "xai" => "xAI · " + metadata.ModelId,
        "bfl" => "BFL · " + metadata.ModelId,
        "managed" => "Local · " + metadata.ModelId,
        _ => metadata.ModelId
    };

    private static ImageSaveReport SaveCompletedImage(string directory, ProviderResult result, string? folder)
    {
        // Saving is never a reason to discard an accepted image or regenerate it.
        ImageSaveReport saved;
        try { saved = new RenderImageStore(RenderImageStore.DefaultFolder).Save(result, folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { saved = new(null, null, ["The automatic image copy could not be saved. Use Save PNG… before closing Rhino."]); }
        try { LocalHistory.SaveResult(directory, result); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { saved = saved with { Problems = saved.Problems.Concat(["Private result history could not be completed; the returned image remains available."]).ToArray() }; }
        return saved;
    }

    private void SetStatus(string value, bool isGuidance = false)
    {
        Status = value;
        StatusIsGuidance = isGuidance;
        if (!closed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private static Task<T> OnUi<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }));
        return completion.Task;
    }

    public static void Shutdown()
    {
        foreach (var s in Sessions.Values)
        {
            s.closed = true;
            s.overlay.TurnOff();
            s.Changed = null;
            s.token = "";
            s.preview = null;
            s.Capture = null;
            s.Result = null;
            s.ImageSave = null;
            s.cancellation?.Cancel();
        }

        Sessions.Clear();
    }

    private sealed record RenderChoice(RenderModelProfile Model, Uri Destination, bool ProjectPinned,
        RepresentationPreset Preset, string UserPrompt, bool IncludeNames, int? SamplingSteps = null)
    {
        public string ProviderId => Model.ProviderId;
    }
    private sealed record PreparedCapture(CapturedScene Capture, byte[] PreparedPng, ResolutionPlan Plan,
        RenderIntent Intent, RenderChoice Choice, string Fingerprint, PreviewGuard Guard);

    private sealed class CallbackProgress(Action<ProviderProgress> action) : IProgress<ProviderProgress>
    {
        public void Report(ProviderProgress value) => action(value);
    }
}

internal static class LocalHistory
{
    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisoLoom", "history");

    public static string SaveCapture(CapturedScene capture, RenderIntent intent, ResolutionPlan plan, ProjectModelPin model,
        bool projectPinned, byte[] preparedReference)
    {
        DirectoryPrivate(Root);
        foreach (var directory in new DirectoryInfo(Root).EnumerateDirectories().Where(x => Guid.TryParseExact(x.Name, "N", out _)).OrderByDescending(x => x.CreationTimeUtc).Skip(19))
        {
            try
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) == 0 &&
                    new RenderImageStore(RenderImageStore.DefaultFolder).PreserveCompletedHistory(directory.FullName))
                    directory.Delete(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // Cleanup cannot block a new capture.
        }
        string path = Path.Combine(Root, capture.Manifest.SceneId.ToString("N"));
        string staging = Path.Combine(Root, ".pending-" + capture.Manifest.SceneId.ToString("N"));
        DirectoryPrivate(staging);
        try
        {
            Write(staging, "scene.json", JsonSerializer.SerializeToUtf8Bytes(capture.Manifest, WireJson.Options));
            Write(staging, "intent.json", JsonSerializer.SerializeToUtf8Bytes(new
            {
                intent, plan, model, selectionMode = projectPinned ? "projectPin" : "session",
                destination = model.ProviderId switch
                {
                    "openai" => "openaiCloud",
                    "google" => "googleCloud",
                    "xai" => "xaiCloud",
                    "bfl" => "bflCloud",
                    _ => "loopbackReferenceServer"
                },
                preparedReferenceSha256 = ContentDigest.Sha256(preparedReference)
            }, WireJson.Options));
            Write(staging, "reference.png", capture.Png);
            Write(staging, "model-reference.png", preparedReference);
            Directory.Move(staging, path);
            return path;
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
        }
    }

    public static void SaveResult(string directory, ProviderResult result)
    {
        if (result.NativePngBytes is { } native)
            Write(directory, "native-result.png", native);
        Write(directory, "result.png", result.PngBytes);
        Write(directory, "result.json", JsonSerializer.SerializeToUtf8Bytes(result.Metadata, WireJson.Options));
    }

    private static void DirectoryPrivate(string path)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("History cannot use symbolic links.");
    }

    private static void Write(string directory, string name, byte[] bytes)
    {
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            using (var file = new FileStream(temporary, options))
            {
                file.Write(bytes);
                file.Flush(true);
            }

            File.Move(temporary, Path.Combine(directory, name), false);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}

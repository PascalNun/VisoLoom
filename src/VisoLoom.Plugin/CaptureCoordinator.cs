using System.Text.Json;
using Rhino;
using VisoLoom.Core;
using VisoLoom.Protocol;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal sealed class CaptureCoordinator
{
    private static readonly Dictionary<uint, CaptureCoordinator> Sessions = [];
    private static bool jobInFlight;
    private readonly uint documentSerial;
    private readonly string sessionId = Guid.NewGuid().ToString("N");
    private string token = "";
    private CancellationTokenSource? cancellation;
    private bool closed;
    private bool automaticConnection;
    private DateTime nextConnectionAttempt;
    private ResolutionPlan? savedPlan;
    private PreparedCapture? preview;
    private string? outputFolder;
    private bool previewing;
    public bool CanReconnect { get; private set; }
    public Uri Endpoint { get; private set; } = new("http://127.0.0.1:5179/");
    public bool IsConnecting { get; private set; }
    public ModelCapabilities? LocalModel { get; private set; }
    private string selectedProvider = "local";
    public string SelectedProvider
    {
        get => selectedProvider;
        set
        {
            if (selectedProvider == value)
                return;
            selectedProvider = value;
            if (IsBusy)
                return;
            if (value == "openai")
                SetStatus(ConnectionSettings.IsReady ? "OpenAI is configured. Preview the selected Rhino view before rendering." : "Connect OpenAI in VisoLoom Settings first.");
            else if (value == "managed")
                SetStatus("Preview the Rhino view, then render with FLUX.2 klein 4B on this computer.");
            else
                SetStatus(token.Length != 0 ? "Connected to the local test service. Ready to test capture." : "Start Test VisoLoom to connect automatically.");
        }
    }
    public RepresentationPreset SelectedPreset { get; set; } = PromptPresets.All[0];
    public string UserPrompt { get; set; } = "";
    public RenderIntent? Intent { get; private set; }
    public string? ActiveProvider { get; private set; }
    public bool IsConfigured => SelectedProvider == "openai" ? ConnectionSettings.IsReady : token.Length != 0 &&
        (SelectedProvider != "managed" || ConnectionSettings.LocalEngineAllowed && LocalModel is not null);
    public bool IsBusy => previewing || cancellation is not null;
    public byte[]? PreviewPng => preview?.PreparedPng;
    public ResolutionPlan? PreviewPlan => preview?.Plan;
    public string? PreviewStyle => preview?.DisplayStyle;
    public bool PreviewSubmitted => preview?.Guard.IsSubmitted == true;
    public int Multiplier { get; set; } = 1;
    public PixelSize? CustomSize { get; set; }
    // Null follows the active view; an ID pins an open model viewport without activating it.
    public Guid? SourceViewportId { get; set; }
    public NamedViewSource NamedView { get; } = new();
    public string Status { get; private set; } = "Start Test VisoLoom to connect automatically. No image generation is connected.";
    public string? HistoryDirectory { get; private set; }
    public CapturedScene? Capture { get; private set; }
    public ProviderResult? Result { get; private set; }
    public ImageSaveReport? ImageSave { get; private set; }

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
                session.Changed = null;
            }
        };
    }

    private CaptureCoordinator(uint serial) => documentSerial = serial;
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
            SetStatus("Named view shown in Rhino. Capture uses the visible viewport frame, style and active clipping.");
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
        if ((long)viewport.Width * size.Height != (long)viewport.Height * size.Width)
            throw new ArgumentException("Custom resolution must preserve the current view's exact aspect ratio.");
        ResolutionPlanner.ValidateSize(size);
        return size;
    }

    private RenderChoice CurrentChoice() => new(SelectedProvider, SelectedPreset, UserPrompt, SelectedProvider == "openai" && ConnectionSettings.IncludeNames);

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
            Choice = CurrentChoice(),
            LocalModelVersion = SelectedProvider == "managed" ? LocalModel?.Version : null,
            Destination = SelectedProvider == "openai" ? "https://api.openai.com/" : Endpoint.AbsoluteUri
        }, WireJson.Options);
    }

    // UI-thread only. No credentials, connection discovery, history writes or provider calls.
    public void Preview(RhinoDoc doc)
    {
        if (IsBusy || jobInFlight || closed || doc.RuntimeSerialNumber != documentSerial)
            return;
        InvalidatePreview();
        previewing = true;
        SetStatus("Preparing a local capture preview…");
        try
        {
            string fingerprint = PreviewFingerprint(doc);
            var view = SourceView(doc);
            var size = RequestedSize(view);
            var choice = CurrentChoice();
            var plan = choice.ProviderId != "local"
                ? ResolutionPlanner.Plan(size, size, choice.ProviderId == "openai" ? OpenAiImageProvider.Model.Resolution : ManagedModel.Create("preview").Resolution)
                : new ResolutionPlan { Capture = size, Native = size, Final = size,
                    ContentRectangle = new(0, 0, size.Width, size.Height), Scale = 1 };
            var captured = SceneBuilder.Capture(doc, view.ActiveViewportID, size, sessionId);
            var intent = choice.ProviderId == "openai"
                ? ScenePromptComposer.Compose(captured.Manifest, choice.Preset, choice.UserPrompt, plan, choice.IncludeNames)
                : choice.ProviderId == "managed" ? ManagedModel.Compose(choice.Preset, choice.UserPrompt, size)
                : new RenderIntent { RequestedOutput = size };
            byte[] prepared = choice.ProviderId != "local"
                ? new RhinoImageProcessor().PrepareReference(captured.Png, plan) : captured.Png;
            if (PngHeader.Read(prepared) != plan.Native ||
                choice.ProviderId == "openai" && prepared.LongLength > OpenAiImageProvider.MaximumInputBytes ||
                choice.ProviderId == "managed" && prepared.LongLength > ManagedModel.MaximumImageBytes)
                throw new ArgumentException("The prepared image exceeds the model's input limits. Choose a smaller size.");
            if (fingerprint != PreviewFingerprint(doc))
                throw new InvalidOperationException("The scene or view changed during preview. Preview the capture again.");
            preview = new(captured, prepared, plan, intent, choice, fingerprint, new(fingerprint), view.ActiveViewport.DisplayMode.LocalName);
            Capture = captured;
            Intent = intent;
            Result = null;
            ImageSave = null;
            HistoryDirectory = null;
            CanReconnect = false;
            SetStatus("Preview ready. Nothing has been sent. Review the image and prepared prompt before rendering.");
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
        automaticConnection = false;
        SetStatus("Local connection configured. Token is held for this session only.");
    }

    // Called on the UI thread; discovery never captures or uploads a Rhino scene.
    public async Task TryConnectAutomaticallyAsync()
    {
        if (closed || SelectedProvider == "openai" || token.Length != 0 || IsBusy || IsConnecting || DateTime.UtcNow < nextConnectionAttempt)
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
                    LocalModel = capabilities.Models.SingleOrDefault(m => m.Id == ManagedModel.Id);
                    if (SelectedProvider == "local")
                        SetStatus("Connected to the local test service. Ready to test capture; no image generation is connected.");
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

    public async Task CheckLocalEngineAsync()
    {
        if (IsBusy || closed) throw new InvalidOperationException("Wait for the current render to finish.");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var connection = await LocalDevelopmentConnection.FindAsync(deadline.Token)
                ?? throw new InvalidOperationException("Start the local test launcher first.");
            using var provider = new LocalProviderClient(connection.Endpoint, connection.Token, modelId: ManagedModel.Id, images: new RhinoImageProcessor());
            var capabilities = await provider.DiscoverAsync(deadline.Token);
            InvalidatePreview();
            LocalModel = capabilities.Models.Single(m => m.Id == ManagedModel.Id);
            Endpoint = connection.Endpoint; token = connection.Token; automaticConnection = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            LocalModel = null;
            Changed?.Invoke(this, EventArgs.Empty);
            throw;
        }
    }

    private async Task RefreshAutomaticConnectionAsync(bool reconnect, CancellationToken ct)
    {
        if (!automaticConnection)
            return;
        var connection = await LocalDevelopmentConnection.FindAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Start Test VisoLoom to reconnect to the local test service.");
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
            SetStatus("A VisoLoom capture test is already running. Wait or cancel it first.");
            return;
        }

        if (!IsConfigured)
        {
            SetStatus(SelectedProvider == "openai" ? "Connect OpenAI in VisoLoom Settings first." : "Start Test VisoLoom and wait for the automatic local connection.");
            return;
        }

        if (doc.RuntimeSerialNumber != documentSerial || closed)
            return;
        SelectedPreset = preset ?? SelectedPreset;
        UserPrompt = userPrompt ?? UserPrompt;
        if (!PreviewIsCurrent(doc))
        {
            SetStatus("Preview the current capture before rendering. Changes need a fresh preview.");
            return;
        }
        var prepared = preview!;
        prepared.Guard.Submit(PreviewFingerprint(doc));
        var choice = prepared.Choice;
        string key = choice.ProviderId == "openai" ? ConnectionSettings.ApiKey : "";
        bool cloudAllowed = ConnectionSettings.CloudAllowed;
        Func<IRenderProvider> createProvider = choice.ProviderId == "openai"
            ? () => new OpenAiImageProvider(key, cloudAllowed, new RhinoImageProcessor())
            : choice.ProviderId == "managed" ? () => new LocalProviderClient(Endpoint, token, modelId: ManagedModel.Id, images: new RhinoImageProcessor())
            : () => new LocalProviderClient(Endpoint, token);
        ActiveProvider = choice.ProviderId;
        jobInFlight = true;
        cancellation = new();
        Result = null;
        ImageSave = null;
        HistoryDirectory = null;
        CanReconnect = false;
        SetStatus(choice.ProviderId == "openai" ? "Checking OpenAI image editing…" : "Checking local server…");
        outputFolder = ImageSavingSettings.OutputFolder;
        _ = RunAsync(prepared, createProvider, outputFolder, cancellation.Token);
    }

    private async Task RunAsync(PreparedCapture prepared, Func<IRenderProvider> createProvider, string? selectedOutputFolder, CancellationToken ct)
    {
        var choice = prepared.Choice;
        var captured = prepared.Capture;
        var plan = prepared.Plan;
        var intent = prepared.Intent;
        try
        {
            if (choice.ProviderId != "openai")
                await RefreshAutomaticConnectionAsync(reconnect: false, ct).ConfigureAwait(false);
            using var provider = createProvider();
            var caps = await provider.DiscoverAsync(ct).ConfigureAwait(false);
            if (choice.ProviderId == "managed" && caps.Models.Single().Version != LocalModel?.Version)
                throw new InvalidOperationException("The local model changed. Check it in Settings and preview again before rendering.");
            if (plan != ResolutionPlanner.Plan(captured.Manifest.Frame, intent.RequestedOutput, caps.Models.Single().Resolution))
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
            string directory = await Task.Run(() => LocalHistory.SaveCapture(captured, intent, plan, choice.ProviderId, prepared.PreparedPng), ct).ConfigureAwait(false);
            await OnUi(() =>
            {
                ct.ThrowIfCancellationRequested();
                var document = closed ? null : RhinoDoc.FromRuntimeSerialNumber(documentSerial);
                if (document is null || PreviewFingerprint(document) != prepared.Fingerprint)
                    throw new InvalidOperationException("The scene or settings changed before submission. Preview again. No image was sent.");
                Capture = captured;
                Intent = intent;
                HistoryDirectory = directory;
                SetStatus(choice.ProviderId == "openai" ? "Sending the captured view and selected context to OpenAI…" : "Uploading reference to this computer…");
                return true;
            }).ConfigureAwait(false);
            var progress = new CallbackProgress(p => RhinoApp.InvokeOnUiThread(new Action(() => SetStatus(p.Stage switch
            {
                "queued" => "Queued on the local server…",
                "rendering" => choice.ProviderId == "managed" ? "Generating locally with FLUX.2 klein 4B…" : "Returning the unchanged reference…",
                "downloading" => "Checking returned PNG…",
                "generating" => "Generating with OpenAI…",
                "preparing" => "Checking the prepared reference for OpenAI…",
                "processing" => "Restoring the complete Rhino frame…",
                _ => "Uploading reference to this computer…"
            }))));
            var assets = new Dictionary<string, byte[]> { ["reference.png"] = captured.Png };
            var result = provider is OpenAiImageProvider openAi
                ? await openAi.RenderPreparedAsync(captured.Manifest, assets, intent, plan, prepared.PreparedPng, progress, ct).ConfigureAwait(false)
                : choice.ProviderId == "managed" && provider is LocalProviderClient local
                ? await local.RenderPreparedAsync(captured.Manifest, assets, intent, plan, prepared.PreparedPng, progress, ct).ConfigureAwait(false)
                : await provider.RenderAsync(captured.Manifest, assets, intent, plan, progress, ct).ConfigureAwait(false);
            var saved = await Task.Run(() => SaveCompletedImage(directory, result, selectedOutputFolder), CancellationToken.None).ConfigureAwait(false);
            await OnUi(() =>
            {
                Result = result;
                ImageSave = saved;
                SetStatus((result.Metadata.IsUnchangedReference ? "Capture test complete — returned image is byte-identical to the reference." : "Image generated. Compare geometry, openings, sections and materials with the captured view.") +
                    (saved.Problems.Count > 0 ? " " + string.Join(" ", saved.Problems) + (saved.SavedPath is null ? " Use Save PNG… before closing Rhino." : "") : " Image saved automatically."));
                return true;
            }).ConfigureAwait(false);
        }
        catch (ProviderException ex)
        {
            await OnUi(() =>
            {
                CanReconnect = choice.ProviderId == "local" && ex.Code is ("connectionLost" or "cancelUnconfirmed");
                SetStatus(ex.Message + (ex.JobId is { } id ? $" Job: {id:D}" : ""));
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
                jobInFlight = false;
                Changed?.Invoke(this, EventArgs.Empty);
                return true;
            }).ConfigureAwait(false);
        }
    }

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
            using var provider = new LocalProviderClient(Endpoint, token);
            // RenderAsync uses the snapshot UUID and identical request bytes; changed capabilities conflict safely.
            var result = await provider.RenderAsync(captured.Manifest, new Dictionary<string, byte[]> { ["reference.png"] = captured.Png }, new RenderIntent { RequestedOutput = plan.Final }, plan, cancellationToken: ct).ConfigureAwait(false);
            var saved = await Task.Run(() => SaveCompletedImage(directory, result, outputFolder)).ConfigureAwait(false);
            await OnUi(() =>
            {
                Result = result;
                ImageSave = saved;
                SetStatus("Capture test complete — returned image is byte-identical to the reference. " +
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
        SetStatus(ActiveProvider == "openai" ? "Stopping local waiting. OpenAI may still finish and charge this request." : "Stopping; awaiting the server's cancellation state…");
    }

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

    private void SetStatus(string value)
    {
        Status = value;
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

    private sealed record RenderChoice(string ProviderId, RepresentationPreset Preset, string UserPrompt, bool IncludeNames);
    private sealed record PreparedCapture(CapturedScene Capture, byte[] PreparedPng, ResolutionPlan Plan,
        RenderIntent Intent, RenderChoice Choice, string Fingerprint, PreviewGuard Guard, string DisplayStyle);

    private sealed class CallbackProgress(Action<ProviderProgress> action) : IProgress<ProviderProgress>
    {
        public void Report(ProviderProgress value) => action(value);
    }
}

internal static class LocalHistory
{
    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisoLoom", "history");

    public static string SaveCapture(CapturedScene capture, RenderIntent intent, ResolutionPlan plan, string providerId = "local", byte[]? preparedReference = null)
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
            Write(staging, "intent.json", JsonSerializer.SerializeToUtf8Bytes(new { intent, plan, destination = providerId == "openai" ? "openaiCloud" : "loopbackReferenceServer",
                preparedReferenceSha256 = preparedReference is null ? null : ContentDigest.Sha256(preparedReference) }, WireJson.Options));
            Write(staging, "reference.png", capture.Png);
            if (preparedReference is not null)
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

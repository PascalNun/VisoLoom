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
    public bool CanReconnect { get; private set; }
    public Uri Endpoint { get; private set; } = new("http://127.0.0.1:5179/");
    public bool IsConnecting { get; private set; }
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
                SetStatus(ConnectionSettings.IsReady ? "OpenAI is configured. Ready to render the selected Rhino view." : "Connect OpenAI in VisoLoom Settings first.");
            else
                SetStatus(token.Length != 0 ? "Connected to the local test service. Ready to test capture." : "Start Test VisoLoom to connect automatically.");
        }
    }
    public RepresentationPreset SelectedPreset { get; set; } = PromptPresets.All[0];
    public string UserPrompt { get; set; } = "";
    public RenderIntent? Intent { get; private set; }
    public string? ActiveProvider { get; private set; }
    public bool IsConfigured => SelectedProvider == "openai" ? ConnectionSettings.IsReady : token.Length != 0;
    public bool IsBusy => cancellation is not null;
    public int Multiplier { get; set; } = 1;
    public PixelSize? CustomSize { get; set; }
    // Null follows the active view; an ID pins an open model viewport without activating it.
    public Guid? SourceViewportId { get; set; }
    public NamedViewSource NamedView { get; } = new();
    public string Status { get; private set; } = "Start Test VisoLoom to connect automatically. No image generation is connected.";
    public string? HistoryDirectory { get; private set; }
    public CapturedScene? Capture { get; private set; }
    public ProviderResult? Result { get; private set; }

    public event EventHandler? Changed;
    static CaptureCoordinator()
    {
        RhinoDoc.CloseDocument += (_, e) =>
        {
            if (Sessions.Remove(e.Document.RuntimeSerialNumber, out var session))
            {
                session.closed = true;
                session.token = "";
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
            NamedView.Show(doc);
            SetStatus("Named view shown in Rhino. Capture uses the visible viewport frame, style and active clipping.");
        }
        catch (InvalidOperationException ex) { SetStatus(ex.Message); }
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
        Endpoint = endpoint;
        token = sessionToken;
        automaticConnection = false;
        SetStatus("Local connection configured. Token is held for this session only.");
    }

    // Called on the UI thread; discovery never captures or uploads a Rhino scene.
    public async Task TryConnectAutomaticallyAsync()
    {
        if (closed || SelectedProvider != "local" || token.Length != 0 || IsBusy || IsConnecting || DateTime.UtcNow < nextConnectionAttempt)
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
            await provider.DiscoverAsync(deadline.Token).ConfigureAwait(false);
            await OnUi(() =>
            {
                if (!closed && token.Length == 0)
                {
                    Endpoint = connection.Endpoint;
                    token = connection.Token;
                    automaticConnection = true;
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
        PixelSize size;
        string signature;
        Guid viewportId;
        bool followActive = SourceViewportId is null && NamedView.Name is null;
        try
        {
            var view = SourceView(doc);
            viewportId = view.ActiveViewportID;
            var viewport = SceneBuilder.ViewportSize(view);
            size = CustomSize ?? ResolutionPlanner.ScaleViewport(viewport, Multiplier);
            if ((long)viewport.Width * size.Height != (long)viewport.Height * size.Width)
                throw new ArgumentException("Custom resolution must preserve the current view's exact aspect ratio.");
            ResolutionPlanner.ValidateSize(size);
            signature = SceneBuilder.ViewSignature(view);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message);
            return;
        }
        catch (ArgumentException ex)
        {
            SetStatus(ex.Message);
            return;
        }
        catch (OverflowException)
        {
            SetStatus("The requested capture dimensions are too large.");
            return;
        }

        var choice = new RenderChoice(SelectedProvider, preset ?? SelectedPreset, userPrompt ?? UserPrompt, ConnectionSettings.IncludeNames);
        string key = choice.ProviderId == "openai" ? ConnectionSettings.ApiKey : "";
        bool cloudAllowed = ConnectionSettings.CloudAllowed;
        Func<IRenderProvider> createProvider = choice.ProviderId == "openai"
            ? () => new OpenAiImageProvider(key, cloudAllowed, new RhinoImageProcessor())
            : () => new LocalProviderClient(Endpoint, token);
        ActiveProvider = choice.ProviderId;
        jobInFlight = true;
        cancellation = new();
        Capture = null;
        Intent = null;
        Result = null;
        HistoryDirectory = null;
        CanReconnect = false;
        SetStatus(choice.ProviderId == "openai" ? "Checking OpenAI image editing…" : "Checking local server…");
        _ = RunAsync(viewportId, followActive, size, signature, choice, createProvider, cancellation.Token);
    }

    private async Task RunAsync(Guid viewportId, bool followActive, PixelSize size, string signature, RenderChoice choice, Func<IRenderProvider> createProvider, CancellationToken ct)
    {
        try
        {
            if (choice.ProviderId == "local")
                await RefreshAutomaticConnectionAsync(reconnect: false, ct).ConfigureAwait(false);
            using var provider = createProvider();
            var caps = await provider.DiscoverAsync(ct).ConfigureAwait(false);
            var plan = ResolutionPlanner.Plan(size, size, caps.Models.Single().Resolution);
            if (choice.ProviderId == "local" && !plan.IsExact)
                throw new InvalidOperationException("The reference server cannot return this capture size unchanged. Choose a smaller resolution.");
            var captured = await OnUi(() =>
            {
                ct.ThrowIfCancellationRequested();
                var document = closed ? null : RhinoDoc.FromRuntimeSerialNumber(documentSerial);
                if (document is null)
                    throw new InvalidOperationException("The source document closed before capture.");
                var source = SourceView(document);
                if ((followActive && document.Views.ActiveView?.ActiveViewportID != viewportId) || source.ActiveViewportID != viewportId ||
                    SceneBuilder.ViewSignature(source) != signature)
                    throw new InvalidOperationException("The source view changed while connecting. Start a new render or capture test.");
                SetStatus("Capturing the selected Rhino view…");
                return SceneBuilder.Capture(document, viewportId, size, sessionId);
            }).ConfigureAwait(false);
            savedPlan = plan;
            var intent = choice.ProviderId == "openai"
                ? ScenePromptComposer.Compose(captured.Manifest, choice.Preset ?? throw new ArgumentException("Choose a representation preset."), choice.UserPrompt, plan, choice.IncludeNames)
                : new RenderIntent { RequestedOutput = size };
            string directory = await Task.Run(() => LocalHistory.SaveCapture(captured, intent, plan, choice.ProviderId), ct).ConfigureAwait(false);
            await OnUi(() =>
            {
                Capture = captured;
                Intent = intent;
                HistoryDirectory = directory;
                SetStatus(choice.ProviderId == "openai" ? "Sending the captured view and selected context to OpenAI…" : "Uploading reference to this computer…");
                return true;
            }).ConfigureAwait(false);
            var progress = new CallbackProgress(p => RhinoApp.InvokeOnUiThread(new Action(() => SetStatus(p.Stage switch
            {
                "queued" => "Queued on the local server…",
                "rendering" => "Returning the unchanged reference…",
                "downloading" => "Checking returned PNG…",
                "generating" => "Generating with OpenAI…",
                "preparing" => "Preparing the complete Rhino frame for OpenAI…",
                "processing" => "Restoring the complete Rhino frame…",
                _ => "Uploading reference to this computer…"
            }))));
            var result = await provider.RenderAsync(captured.Manifest, new Dictionary<string, byte[]> { ["reference.png"] = captured.Png }, intent, plan, progress, ct).ConfigureAwait(false);
            await Task.Run(() => LocalHistory.SaveResult(directory, result), CancellationToken.None).ConfigureAwait(false);
            await OnUi(() =>
            {
                Result = result;
                SetStatus(result.Metadata.IsUnchangedReference ? "Capture test complete — returned image is byte-identical to the reference." : "Image generated. Compare geometry, openings, sections and materials with the captured view.");
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
            await Task.Run(() => LocalHistory.SaveResult(directory, result)).ConfigureAwait(false);
            await OnUi(() =>
            {
                Result = result;
                SetStatus("Capture test complete — returned image is byte-identical to the reference.");
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
        cancellation?.Cancel();
        SetStatus(ActiveProvider == "openai" ? "Stopping local waiting. OpenAI may still finish and charge this request." : "Stopping; awaiting the server's cancellation state…");
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
            s.cancellation?.Cancel();
        }

        Sessions.Clear();
    }

    private sealed record RenderChoice(string ProviderId, RepresentationPreset? Preset, string UserPrompt, bool IncludeNames);

    private sealed class CallbackProgress(Action<ProviderProgress> action) : IProgress<ProviderProgress>
    {
        public void Report(ProviderProgress value) => action(value);
    }
}

internal static class LocalHistory
{
    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisoLoom", "history");

    public static string SaveCapture(CapturedScene capture, RenderIntent intent, ResolutionPlan plan, string providerId = "local")
    {
        DirectoryPrivate(Root);
        foreach (var directory in new DirectoryInfo(Root).EnumerateDirectories().Where(x => Guid.TryParseExact(x.Name, "N", out _)).OrderByDescending(x => x.CreationTimeUtc).Skip(19))
            if ((directory.Attributes & FileAttributes.ReparsePoint) == 0)
                directory.Delete(true);
        string path = Path.Combine(Root, capture.Manifest.SceneId.ToString("N"));
        string staging = Path.Combine(Root, ".pending-" + capture.Manifest.SceneId.ToString("N"));
        DirectoryPrivate(staging);
        try
        {
            Write(staging, "scene.json", JsonSerializer.SerializeToUtf8Bytes(capture.Manifest, WireJson.Options));
            Write(staging, "intent.json", JsonSerializer.SerializeToUtf8Bytes(new { intent, plan, destination = providerId == "openai" ? "openaiCloud" : "loopbackReferenceServer" }, WireJson.Options));
            Write(staging, "reference.png", capture.Png);
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

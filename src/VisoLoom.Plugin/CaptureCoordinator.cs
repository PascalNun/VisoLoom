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
    private ResolutionPlan? savedPlan;
    public bool CanReconnect { get; private set; }
    public Uri Endpoint { get; private set; } = new("http://127.0.0.1:5179/");
    public bool IsConfigured => token.Length != 0;
    public bool IsBusy => cancellation is not null;
    public int Multiplier { get; set; } = 1;
    public PixelSize? CustomSize { get; set; }
    public string Status { get; private set; } = "Ready to test capture. No image generation is connected.";
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
    public static CaptureCoordinator ForDocument(uint serial)
    {
        if (!Sessions.TryGetValue(serial, out var session))
            Sessions.Add(serial, session = new(serial));
        return session;
    }

    public void Configure(Uri endpoint, string sessionToken)
    {
        if (IsBusy)
            throw new InvalidOperationException("Wait for the current capture test to finish.");
        EndpointPolicy.RequireLocalEndpoint(endpoint);
        if (sessionToken.Length < 32 || sessionToken.Any(char.IsControl))
            throw new ArgumentException("Enter the server's session token (at least 32 characters).");
        if (CanReconnect && endpoint != Endpoint)
            throw new ArgumentException("Reconnect uses the original local server. Start a new capture test to change destinations.");
        Endpoint = endpoint;
        token = sessionToken;
        SetStatus("Local connection configured. Token is held for this session only.");
    }

    public void Start(RhinoDoc doc)
    {
        if (jobInFlight)
        {
            SetStatus("A VisoLoom capture test is already running. Wait or cancel it first.");
            return;
        }

        if (!IsConfigured)
        {
            SetStatus("Use Change to connect to the local reference server.");
            return;
        }

        if (doc.RuntimeSerialNumber != documentSerial || closed)
            return;
        var view = doc.Views.ActiveView;
        if (view is null)
        {
            SetStatus("Activate a Rhino model viewport first.");
            return;
        }

        PixelSize size;
        string signature;
        try
        {
            var viewport = SceneBuilder.ViewportSize(doc);
            size = CustomSize ?? ResolutionPlanner.ScaleViewport(viewport, Multiplier);
            if ((long)viewport.Width * size.Height != (long)viewport.Height * size.Width)
                throw new ArgumentException("Custom resolution must preserve the current view's exact aspect ratio.");
            ResolutionPlanner.ValidateSize(size);
            signature = SceneBuilder.ViewSignature(doc);
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

        jobInFlight = true;
        cancellation = new();
        Capture = null;
        Result = null;
        HistoryDirectory = null;
        CanReconnect = false;
        SetStatus("Checking local server…");
        _ = RunAsync(view.ActiveViewportID, size, signature, cancellation.Token);
    }

    private async Task RunAsync(Guid viewportId, PixelSize size, string signature, CancellationToken ct)
    {
        try
        {
            using var provider = new LocalProviderClient(Endpoint, token);
            var caps = await provider.DiscoverAsync(ct).ConfigureAwait(false);
            var plan = ResolutionPlanner.Plan(size, size, caps.Models.Single(m => m.Id == ReferenceCapabilities.ModelId).Resolution);
            if (!plan.IsExact)
                throw new InvalidOperationException("The reference server cannot return this capture size unchanged. Choose a smaller resolution.");
            var captured = await OnUi(() =>
            {
                ct.ThrowIfCancellationRequested();
                var document = closed ? null : RhinoDoc.FromRuntimeSerialNumber(documentSerial);
                if (document is null)
                    throw new InvalidOperationException("The source document closed before capture.");
                if (SceneBuilder.ViewSignature(document) != signature)
                    throw new InvalidOperationException("The Rhino view changed while connecting. Start a new capture test.");
                SetStatus("Capturing the current Rhino view…");
                return SceneBuilder.Capture(document, viewportId, size, sessionId);
            }).ConfigureAwait(false);
            savedPlan = plan;
            var intent = new RenderIntent
            {
                RequestedOutput = size
            };
            string directory = await Task.Run(() => LocalHistory.SaveCapture(captured, intent, plan), ct).ConfigureAwait(false);
            await OnUi(() =>
            {
                Capture = captured;
                HistoryDirectory = directory;
                SetStatus("Uploading reference to this computer…");
                return true;
            }).ConfigureAwait(false);
            var progress = new CallbackProgress(p => RhinoApp.InvokeOnUiThread(new Action(() => SetStatus(p.Stage switch
            {
                "queued" => "Queued on the local server…",
                "rendering" => "Returning the unchanged reference…",
                "downloading" => "Checking returned PNG…",
                _ => "Uploading reference to this computer…"
            }))));
            var result = await provider.RenderAsync(captured.Manifest, new Dictionary<string, byte[]> { ["reference.png"] = captured.Png }, intent, plan, progress, ct).ConfigureAwait(false);
            await Task.Run(() => LocalHistory.SaveResult(directory, result), CancellationToken.None).ConfigureAwait(false);
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
                SetStatus(ex.Message + (ex.JobId is { } id ? $" Job: {id:D}" : ""));
                return true;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await OnUi(() =>
            {
                SetStatus("Capture test stopped before submission.");
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
                SetStatus("Capture test failed. Check the local server, image support and available disk space.");
                return true;
            }).ConfigureAwait(false);
        }
        finally
        {
            await OnUi(() =>
            {
                cancellation?.Dispose();
                cancellation = null;
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
                jobInFlight = false;
                Changed?.Invoke(this, EventArgs.Empty);
                return true;
            }).ConfigureAwait(false);
        }
    }

    public void Cancel()
    {
        cancellation?.Cancel();
        SetStatus("Stopping; awaiting the server's cancellation state…");
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
            s.token = "";
            s.cancellation?.Cancel();
        }

        Sessions.Clear();
    }

    private sealed class CallbackProgress(Action<ProviderProgress> action) : IProgress<ProviderProgress>
    {
        public void Report(ProviderProgress value) => action(value);
    }
}

internal static class LocalHistory
{
    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisoLoom", "history");

    public static string SaveCapture(CapturedScene capture, RenderIntent intent, ResolutionPlan plan)
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
            Write(staging, "intent.json", JsonSerializer.SerializeToUtf8Bytes(new { intent, plan, destination = "loopbackReferenceServer" }, WireJson.Options));
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

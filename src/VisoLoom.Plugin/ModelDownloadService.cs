using Eto.Forms;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

// Model downloads belong to the Rhino session, not to the Settings window:
// a user starts a package, closes Settings and keeps modelling while the
// bytes stream in. Exactly one download runs at a time; every Settings
// surface subscribes and renders what the download is doing whenever it is
// open. Cancelling — or Rhino shutting down — walks the installer's graceful
// stop, so no partial file survives any exit path.
internal sealed class ModelDownloadService
{
    public static ModelDownloadService Instance { get; } = new();

    private readonly LocalModelInstaller installer = new();
    private readonly LocalModelLibrary library = new();
    private readonly object gate = new();
    private CancellationTokenSource? source;
    private DownloadState? current;
    private DownloadOutcome? outcome;

    // The live download, null while idle. The status line is composed here
    // so every Settings surface renders the same sentence.
    public sealed record DownloadState(string PackageId, long PackageBytes, long PackageBytesDone, string StatusLine)
    {
        // One-thousandth precision, the progress bar's native range.
        public int Thousandths => PackageBytes > 0 ? (int)(PackageBytesDone * 1000 / PackageBytes) : 0;
    }

    // What a finished download left behind: shown once by a Changed
    // observer, then consumed.
    public sealed record DownloadOutcome(string PackageId, bool Verified, string Message);

    public event EventHandler? Changed;

    private ModelDownloadService() { }

    public DownloadState? Current
    {
        get { lock (gate) return current; }
    }

    public DownloadOutcome? TakeOutcome()
    {
        lock (gate)
        {
            var finished = outcome;
            outcome = null;
            return finished;
        }
    }

    // The size-and-destination confirmation stays with the Settings UI that
    // owns the click; the verified download itself is session work.
    public void Start(ReviewedModelPackage package, string destination)
    {
        CancellationTokenSource started;
        lock (gate)
        {
            if (current is not null) return;
            source = new CancellationTokenSource();
            started = source;
            current = new DownloadState(package.Id, package.TotalBytes, 0,
                "Downloading " + BytesDisplay(package.TotalBytes) + "…");
            outcome = null;
        }
        Raise();
        _ = RunAsync(package, destination, started.Token);
    }

    // Cancel is named per row: the id belongs to the download being stopped.
    public void Cancel(string packageId)
    {
        lock (gate)
        {
            if (current is not { PackageId: var id } || id != packageId) return;
            current = current with { StatusLine = "Stopping…" };
            source?.Cancel();
        }
        Raise();
    }

    // Rhino is quitting: tell the running download to stop; the installer's
    // stop-marker path removes every staged byte before its process exits.
    public void Shutdown() => (Interlocked.Exchange(ref source, null))?.Cancel();

    private async Task RunAsync(ReviewedModelPackage package, string destination, CancellationToken token)
    {
        bool verified = false;
        string message;
        try
        {
            var result = await installer.InstallAsync(package.Id, destination, installer.ReceiptPath(package.Id),
                progress => UpdateProgress(progress), token, ConnectionSettings.HuggingFaceToken).ConfigureAwait(false);
            await library.AddAsync(result.Destination, CancellationToken.None).ConfigureAwait(false);
            verified = true;
            message = "Downloaded and verified. Check the local engine to make the model available in Render.";
        }
        catch (ProviderException failure)
        {
            message = failure.Message;
        }
        catch (OperationCanceledException)
        {
            message = "Download stopped.";
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            message = "Could not complete the download. Check the folder and try again.";
        }
        lock (gate)
        {
            current = null;
            outcome = new DownloadOutcome(package.Id, verified, message);
        }
        Raise();
    }

    private void UpdateProgress(ReviewedModelProgress progress)
    {
        lock (gate)
        {
            if (current is not { } running) return;
            if (progress.BytesDone is { } done)
            {
                var updated = running with
                {
                    PackageBytesDone = progress.PackageBytesDone ?? running.PackageBytesDone,
                };
                current = updated with
                {
                    StatusLine = "Downloading " + progress.Index + " of " + progress.FileCount + " · " +
                        BytesDisplay(done) + " of " + BytesDisplay(progress.Bytes) +
                        " (" + updated.Thousandths / 10 + "%)",
                };
            }
            else
                current = running with
                {
                    StatusLine = "Received " + progress.Index + " of " + progress.FileCount + " · " + progress.Name,
                };
        }
        Raise();
    }

    private void Raise()
    {
        if (Application.Instance is { } app)
            app.AsyncInvoke(() => Changed?.Invoke(this, EventArgs.Empty));
        else
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public static string BytesDisplay(long bytes) => bytes switch
    {
        >= 1_000_000_000 => (bytes / 1_000_000_000d).ToString("0.##") + " GB",
        >= 1_000_000 => (bytes / 1_000_000d).ToString("0") + " MB",
        >= 1_000 => (bytes / 1_000d).ToString("0") + " KB",
        _ => bytes.ToString() + " B",
    };
}

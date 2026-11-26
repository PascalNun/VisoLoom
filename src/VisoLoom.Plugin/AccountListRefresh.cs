using Eto.Forms;
using VisoLoom.Core;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

// A saved key is a connection, and the account's model list is the menu's
// source: every Rhino session refreshes each connected provider's list once
// in the background, so the newest image models appear without a plugin
// update and without a manual Settings visit. The last successful list is
// persisted (ConnectionSettings) and shown immediately at start; a refresh
// that fails is silent — the Settings connection check stays the place a
// reason is read — and the cached list keeps serving.
internal static class AccountListRefresh
{
    private static int inFlight;
    private static int done;

    // Rhino's start and every panel open call this; one pass serves a
    // session once it published any provider's list.
    public static void RunOnce()
    {
        if (Volatile.Read(ref done) == 1 || Interlocked.Exchange(ref inFlight, 1) == 1)
            return;
        _ = Task.Run(async () =>
        {
            bool published = false;
            try
            {
                var providers = new List<Task<bool>>();
                if (ConnectionSettings.IsReady)
                    providers.Add(RefreshAsync(new OpenAiImageProvider(ConnectionSettings.ApiKey, true, new RhinoImageProcessor()), ConnectionSettings.SetAvailableOpenAiModels));
                if (ConnectionSettings.GoogleIsReady)
                    providers.Add(RefreshAsync(new GoogleImageProvider(ConnectionSettings.GoogleApiKey, true, new RhinoImageProcessor()), ConnectionSettings.SetAvailableGoogleModels));
                if (ConnectionSettings.XaiIsReady)
                    providers.Add(RefreshAsync(new XaiImageProvider(ConnectionSettings.XaiApiKey, true, new RhinoImageProcessor()), ConnectionSettings.SetAvailableXaiModels));
                if (ConnectionSettings.BflIsReady)
                    providers.Add(RefreshAsync(new BflImageProvider(ConnectionSettings.BflApiKey, true, new RhinoImageProcessor()), ConnectionSettings.SetAvailableBflModels));
                published = (await Task.WhenAll(providers).ConfigureAwait(false)).Any(listed => listed);
            }
            finally
            {
                if (published)
                    Volatile.Write(ref done, 1);
                Volatile.Write(ref inFlight, 0);
            }
        });
    }

    private static async Task<bool> RefreshAsync(IRenderProvider provider, Action<IReadOnlyList<ModelCapabilities>> publish)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var capabilities = await provider.DiscoverAsync(timeout.Token).ConfigureAwait(false);
            var models = capabilities.Models;
            // The panel listens to the published list; publishing lands on
            // the UI thread so the menu rebuild never races Eto.
            if (Application.Instance is { } app)
                app.AsyncInvoke(() => publish(models));
            else
                publish(models);
            return models.Count != 0;
        }
        catch
        {
            // A failed refresh keeps the persisted list; Settings remains the
            // place reasons are shown.
            return false;
        }
    }
}

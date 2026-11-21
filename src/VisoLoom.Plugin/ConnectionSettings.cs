using Rhino;
using VisoLoom.Core;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal static class ConnectionSettings
{
    private const string CredentialOrigin = "openai/https://api.openai.com";
    private const string GoogleCredentialOrigin = "google/https://generativelanguage.googleapis.com";
    private const string XaiCredentialOrigin = "xai/https://api.x.ai";
    private const string BflCredentialOrigin = "bfl/https://api.bfl.ai";
    private static readonly ICredentialStore Credentials = new OsCredentialStore();
    private static string apiKey = "";
    private static string googleApiKey = "";
    private static string xaiApiKey = "";
    private static string bflApiKey = "";
    private static IReadOnlyList<ModelCapabilities> availableOpenAiModels = [];
    private static IReadOnlyList<ModelCapabilities> availableGoogleModels = [];
    private static IReadOnlyList<ModelCapabilities> availableXaiModels = [];
    private static IReadOnlyList<ModelCapabilities> availableBflModels = [];
    // The image models the account listed at the most recent successful
    // connection check. The Model menu offers exactly these.
    public static IReadOnlyList<ModelCapabilities> AvailableOpenAiModels => availableOpenAiModels;
    public static IReadOnlyList<ModelCapabilities> AvailableGoogleModels => availableGoogleModels;
    public static IReadOnlyList<ModelCapabilities> AvailableXaiModels => availableXaiModels;
    public static IReadOnlyList<ModelCapabilities> AvailableBflModels => availableBflModels;
    public static bool CloudAllowed { get; private set; }
    public static bool GoogleAllowed { get; private set; }
    public static bool XaiAllowed { get; private set; }
    public static bool BflAllowed { get; private set; }
    public static bool IncludeNames { get; private set; }
    public static bool RememberKey { get; private set; }
    public static bool GoogleRememberKey { get; private set; }
    public static bool XaiRememberKey { get; private set; }
    public static bool BflRememberKey { get; private set; }
    public static bool LocalEngineAllowed { get; private set; }
    public static long Revision { get; private set; }
    public static bool IsReady => CloudAllowed && apiKey.Length != 0;
    public static bool GoogleIsReady => GoogleAllowed && googleApiKey.Length != 0;
    public static bool XaiIsReady => XaiAllowed && xaiApiKey.Length != 0;
    public static bool BflIsReady => BflAllowed && bflApiKey.Length != 0;
    public static string ApiKey => apiKey;
    public static string GoogleApiKey => googleApiKey;
    public static string XaiApiKey => xaiApiKey;
    public static string BflApiKey => bflApiKey;
    public static event EventHandler? Changed;
    private static PersistentSettings Preferences => VisoLoomPlugIn.Instance.Settings;

    public static void SetAvailableOpenAiModels(IReadOnlyList<ModelCapabilities> models)
    {
        availableOpenAiModels = models;
        NotifyChanged();
    }

    public static void SetAvailableGoogleModels(IReadOnlyList<ModelCapabilities> models)
    {
        availableGoogleModels = models;
        NotifyChanged();
    }

    public static void SetAvailableXaiModels(IReadOnlyList<ModelCapabilities> models)
    {
        availableXaiModels = models;
        NotifyChanged();
    }

    public static void SetAvailableBflModels(IReadOnlyList<ModelCapabilities> models)
    {
        availableBflModels = models;
        NotifyChanged();
    }

    public static void Load()
    {
        LocalEngineAllowed = Preferences.GetBool("LocalEngine.Allowed", false);
        CloudAllowed = Preferences.GetBool("OpenAI.CloudAllowed", false);
        IncludeNames = Preferences.GetBool("OpenAI.IncludeNames", false);
        RememberKey = Preferences.GetBool("OpenAI.RememberKey", false);
        GoogleAllowed = Preferences.GetBool("Google.Allowed", false);
        GoogleRememberKey = Preferences.GetBool("Google.RememberKey", false);
        XaiAllowed = Preferences.GetBool("Xai.Allowed", false);
        XaiRememberKey = Preferences.GetBool("Xai.RememberKey", false);
        BflAllowed = Preferences.GetBool("Bfl.Allowed", false);
        BflRememberKey = Preferences.GetBool("Bfl.RememberKey", false);
        if (RememberKey && CloudAllowed)
        {
            try
            { apiKey = Credentials.Read(CredentialOrigin) ?? ""; }
            catch (Exception ex) when (StoreUnavailable(ex)) { apiKey = ""; }
        }
        if (GoogleRememberKey && GoogleAllowed)
        {
            try
            { googleApiKey = Credentials.Read(GoogleCredentialOrigin) ?? ""; }
            catch (Exception ex) when (StoreUnavailable(ex)) { googleApiKey = ""; }
        }
        if (XaiRememberKey && XaiAllowed)
        {
            try
            { xaiApiKey = Credentials.Read(XaiCredentialOrigin) ?? ""; }
            catch (Exception ex) when (StoreUnavailable(ex)) { xaiApiKey = ""; }
        }
        if (BflRememberKey && BflAllowed)
        {
            try
            { bflApiKey = Credentials.Read(BflCredentialOrigin) ?? ""; }
            catch (Exception ex) when (StoreUnavailable(ex)) { bflApiKey = ""; }
        }
    }

    public static string ConfigureGoogle(string key, bool remember)
    {
        bool persisted = false;
        if (remember)
        {
            try
            { Credentials.Set(GoogleCredentialOrigin, key); persisted = true; }
            catch (Exception ex) when (StoreUnavailable(ex))
            {
                if (GoogleRememberKey)
                    throw; // Do not silently leave a previous saved key behind.
            }
        }
        else if (GoogleRememberKey)
        {
            // Choosing session-only must also remove a previous saved credential.
            Credentials.Forget(GoogleCredentialOrigin);
        }
        googleApiKey = key;
        GoogleAllowed = true;
        GoogleRememberKey = persisted;
        Save();
        NotifyChanged();
        return persisted ? "Connected. API key saved in your OS credential store." :
            remember ? "Connected for this Rhino session. The OS could not save the key; no plaintext copy was made." :
            "Connected for this Rhino session only.";
    }

    public static void DisableGoogle()
    {
        GoogleAllowed = false;
        availableGoogleModels = [];
        Save();
        NotifyChanged();
    }

    public static string ConfigureXai(string key, bool remember)
    {
        bool persisted = false;
        if (remember)
        {
            try
            { Credentials.Set(XaiCredentialOrigin, key); persisted = true; }
            catch (Exception ex) when (StoreUnavailable(ex))
            {
                if (XaiRememberKey)
                    throw; // Do not silently leave a previous saved key behind.
            }
        }
        else if (XaiRememberKey)
        {
            // Choosing session-only must also remove a previous saved credential.
            Credentials.Forget(XaiCredentialOrigin);
        }
        xaiApiKey = key;
        XaiAllowed = true;
        XaiRememberKey = persisted;
        Save();
        NotifyChanged();
        return persisted ? "Connected. API key saved in your OS credential store." :
            remember ? "Connected for this Rhino session. The OS could not save the key; no plaintext copy was made." :
            "Connected for this Rhino session only.";
    }

    public static void DisableXai()
    {
        XaiAllowed = false;
        availableXaiModels = [];
        Save();
        NotifyChanged();
    }

    public static string ConfigureBfl(string key, bool remember)
    {
        bool persisted = false;
        if (remember)
        {
            try
            { Credentials.Set(BflCredentialOrigin, key); persisted = true; }
            catch (Exception ex) when (StoreUnavailable(ex))
            {
                if (BflRememberKey)
                    throw; // Do not silently leave a previous saved key behind.
            }
        }
        else if (BflRememberKey)
        {
            // Choosing session-only must also remove a previous saved credential.
            Credentials.Forget(BflCredentialOrigin);
        }
        bflApiKey = key;
        BflAllowed = true;
        BflRememberKey = persisted;
        Save();
        NotifyChanged();
        return persisted ? "Connected. API key saved in your OS credential store." :
            remember ? "Connected for this Rhino session. The OS could not save the key; no plaintext copy was made." :
            "Connected for this Rhino session only.";
    }

    public static void DisableBfl()
    {
        BflAllowed = false;
        availableBflModels = [];
        Save();
        NotifyChanged();
    }

    public static void DisconnectBfl()
    {
        // Disable new submissions even if OS deletion fails; report failure to the settings UI.
        bflApiKey = "";
        availableBflModels = [];
        BflAllowed = false;
        Save();
        NotifyChanged();
        if (BflRememberKey)
            Credentials.Forget(BflCredentialOrigin);
        BflRememberKey = false;
        Save();
    }

    public static void DisconnectXai()
    {
        // Disable new submissions even if OS deletion fails; report failure to the settings UI.
        xaiApiKey = "";
        availableXaiModels = [];
        XaiAllowed = false;
        Save();
        NotifyChanged();
        if (XaiRememberKey)
            Credentials.Forget(XaiCredentialOrigin);
        XaiRememberKey = false;
        Save();
    }

    public static void DisconnectGoogle()
    {
        // Disable new submissions even if OS deletion fails; report failure to the settings UI.
        googleApiKey = "";
        availableGoogleModels = [];
        GoogleAllowed = false;
        Save();
        NotifyChanged();
        if (GoogleRememberKey)
            Credentials.Forget(GoogleCredentialOrigin);
        GoogleRememberKey = false;
        Save();
    }

    public static string Configure(string key, bool includeNames, bool remember)
    {
        bool persisted = false;
        if (remember)
        {
            try
            { Credentials.Set(CredentialOrigin, key); persisted = true; }
            catch (Exception ex) when (StoreUnavailable(ex))
            {
                if (RememberKey)
                    throw; // Do not silently leave a previous saved key behind.
            }
        }
        else
        {
            // Choosing session-only must also remove a previous saved credential.
            if (RememberKey)
                Credentials.Forget(CredentialOrigin);
        }
        apiKey = key;
        CloudAllowed = true;
        IncludeNames = includeNames;
        RememberKey = persisted;
        Save();
        NotifyChanged();
        return persisted ? "Connected. API key saved in your OS credential store." :
            remember ? "Connected for this Rhino session. The OS could not save the key; no plaintext copy was made." :
            "Connected for this Rhino session only.";
    }

    public static void Disconnect()
    {
        // Disable new submissions even if OS deletion fails; report failure to the settings UI.
        apiKey = "";
        availableOpenAiModels = [];
        CloudAllowed = false;
        Save();
        NotifyChanged();
        if (RememberKey)
            Credentials.Forget(CredentialOrigin);
        RememberKey = false;
        Save();
    }

    public static void DisableCloud()
    {
        CloudAllowed = false;
        availableOpenAiModels = [];
        Save();
        NotifyChanged();
    }

    public static void ClearSession()
    {
        apiKey = "";
        googleApiKey = "";
        xaiApiKey = "";
        bflApiKey = "";
        availableOpenAiModels = [];
        availableGoogleModels = [];
        availableXaiModels = [];
        availableBflModels = [];
        NotifyChanged();
    }
    public static void AllowLocalEngine(bool allowed)
    {
        LocalEngineAllowed = allowed;
        Preferences.SetBool("LocalEngine.Allowed", allowed);
        NotifyChanged();
    }
    private static void NotifyChanged()
    {
        Revision++;
        Changed?.Invoke(null, EventArgs.Empty);
    }
    private static void Save()
    {
        Preferences.SetBool("OpenAI.CloudAllowed", CloudAllowed);
        Preferences.SetBool("OpenAI.IncludeNames", IncludeNames);
        Preferences.SetBool("OpenAI.RememberKey", RememberKey);
        Preferences.SetBool("Google.Allowed", GoogleAllowed);
        Preferences.SetBool("Google.RememberKey", GoogleRememberKey);
        Preferences.SetBool("Xai.Allowed", XaiAllowed);
        Preferences.SetBool("Xai.RememberKey", XaiRememberKey);
        Preferences.SetBool("Bfl.Allowed", BflAllowed);
        Preferences.SetBool("Bfl.RememberKey", BflRememberKey);
    }
    internal static bool StoreUnavailable(Exception ex) => ex is InvalidOperationException or NotSupportedException or DllNotFoundException or EntryPointNotFoundException or TypeInitializationException;
}

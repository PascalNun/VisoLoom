using Rhino;
using VisoLoom.Core;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

internal static class ConnectionSettings
{
    private const string CredentialOrigin = "openai/https://api.openai.com";
    private static readonly ICredentialStore Credentials = new OsCredentialStore();
    private static string apiKey = "";
    private static IReadOnlyList<ModelCapabilities> availableOpenAiModels = [];
    // The image models the account listed at the most recent successful
    // connection check. The Model menu offers exactly these, newest first.
    public static IReadOnlyList<ModelCapabilities> AvailableOpenAiModels => availableOpenAiModels;
    public static bool CloudAllowed { get; private set; }
    public static bool IncludeNames { get; private set; }
    public static bool RememberKey { get; private set; }
    public static bool LocalEngineAllowed { get; private set; }
    public static long Revision { get; private set; }
    public static bool IsReady => CloudAllowed && apiKey.Length != 0;
    public static string ApiKey => apiKey;
    public static event EventHandler? Changed;
    private static PersistentSettings Preferences => VisoLoomPlugIn.Instance.Settings;

    public static void SetAvailableOpenAiModels(IReadOnlyList<ModelCapabilities> models)
    {
        availableOpenAiModels = models;
        NotifyChanged();
    }

    public static void Load()
    {
        LocalEngineAllowed = Preferences.GetBool("LocalEngine.Allowed", false);
        CloudAllowed = Preferences.GetBool("OpenAI.CloudAllowed", false);
        IncludeNames = Preferences.GetBool("OpenAI.IncludeNames", false);
        RememberKey = Preferences.GetBool("OpenAI.RememberKey", false);
        if (!RememberKey || !CloudAllowed)
            return;
        try
        { apiKey = Credentials.Read(CredentialOrigin) ?? ""; }
        catch (Exception ex) when (StoreUnavailable(ex)) { apiKey = ""; }
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
        availableOpenAiModels = [];
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
    }
    internal static bool StoreUnavailable(Exception ex) => ex is InvalidOperationException or NotSupportedException or DllNotFoundException or EntryPointNotFoundException or TypeInitializationException;
}

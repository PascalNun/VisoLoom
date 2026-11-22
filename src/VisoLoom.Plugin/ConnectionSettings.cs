using System.Text.Json;
using Rhino;
using VisoLoom.Core;
using VisoLoom.Providers;
using VisoLoom.Protocol;

namespace VisoLoom.Plugin;

internal static class ConnectionSettings
{
    private const string CredentialOrigin = "openai/https://api.openai.com";
    private const string GoogleCredentialOrigin = "google/https://generativelanguage.googleapis.com";
    private const string XaiCredentialOrigin = "xai/https://api.x.ai";
    private const string BflCredentialOrigin = "bfl/https://api.bfl.ai";
    private const string HuggingFaceCredentialOrigin = "huggingface/https://huggingface.co";
    private const string ServerCredentialOrigin = "visoLoom/server";
    private static string serverToken = "";
    private static readonly ICredentialStore Credentials = new OsCredentialStore();
    private static string apiKey = "";
    private static string googleApiKey = "";
    private static string xaiApiKey = "";
    private static string bflApiKey = "";
    // The download token for gated Hugging Face weights. It is a credential
    // like the provider keys: stored in the OS store, entered by the user,
    // and sent only to pinned huggingface.co files (ADR 0032).
    private static string huggingFaceToken = "";
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
    public static bool IncludeNames { get; private set; }
    public static long Revision { get; private set; }
    // A stored key is a connection; connecting is the consent to render.
    public static bool IsReady => apiKey.Length != 0;
    public static bool GoogleIsReady => googleApiKey.Length != 0;
    public static bool XaiIsReady => xaiApiKey.Length != 0;
    public static bool BflIsReady => bflApiKey.Length != 0;
    public static bool HuggingFaceIsReady => huggingFaceToken.Length != 0;
    public static string ApiKey => apiKey;
    public static string GoogleApiKey => googleApiKey;
    public static string XaiApiKey => xaiApiKey;
    public static string BflApiKey => bflApiKey;
    public static string HuggingFaceToken => huggingFaceToken;
    public static string ServerAddress { get; private set; } = "";
    public static string ServerToken => serverToken;
    // A server is connected once a check of this Rhino session confirmed it.
    public static bool ServerConnected { get; private set; }
    public static string ServerModel { get; private set; } = "";
    public static event EventHandler? Changed;
    // The fixture assembly has no registered plug-in; dereference fails
    // loudly there instead of silently reading nothing.
    private static PersistentSettings Preferences => VisoLoomPlugIn.Instance!.Settings;

    public static void SetAvailableOpenAiModels(IReadOnlyList<ModelCapabilities> models)
    {
        availableOpenAiModels = models;
        WriteListedModels(OpenAiListedKey, models);
        NotifyChanged();
    }

    public static void SetAvailableGoogleModels(IReadOnlyList<ModelCapabilities> models)
    {
        availableGoogleModels = models;
        WriteListedModels(GoogleListedKey, models);
        NotifyChanged();
    }

    public static void SetAvailableXaiModels(IReadOnlyList<ModelCapabilities> models)
    {
        availableXaiModels = models;
        WriteListedModels(XaiListedKey, models);
        NotifyChanged();
    }

    public static void SetAvailableBflModels(IReadOnlyList<ModelCapabilities> models)
    {
        availableBflModels = models;
        WriteListedModels(BflListedKey, models);
        NotifyChanged();
    }

    public static void Load()
    {
        IncludeNames = Preferences.GetBool("OpenAI.IncludeNames", false);
        apiKey = ReadKey(CredentialOrigin);
        googleApiKey = ReadKey(GoogleCredentialOrigin);
        xaiApiKey = ReadKey(XaiCredentialOrigin);
        bflApiKey = ReadKey(BflCredentialOrigin);
        huggingFaceToken = ReadKey(HuggingFaceCredentialOrigin);
        ServerAddress = Preferences.GetString("Server.Address", "") ?? "";
        serverToken = ReadKey(ServerCredentialOrigin);
        // The last successful model lists are the session's starting menu: the
        // background refresh (AccountListRefresh) keeps them current online.
        availableOpenAiModels = ReadListedModels(OpenAiListedKey);
        availableGoogleModels = ReadListedModels(GoogleListedKey);
        availableXaiModels = ReadListedModels(XaiListedKey);
        availableBflModels = ReadListedModels(BflListedKey);
    }

    public static string Configure(string key, bool includeNames)
    {
        IncludeNames = includeNames;
        return Connect(ref apiKey, CredentialOrigin, key);
    }

    public static string ConfigureGoogle(string key) => Connect(ref googleApiKey, GoogleCredentialOrigin, key);

    public static string ConfigureXai(string key) => Connect(ref xaiApiKey, XaiCredentialOrigin, key);

    public static string ConfigureBfl(string key) => Connect(ref bflApiKey, BflCredentialOrigin, key);

    public static void Disconnect()
    {
        apiKey = "";
        availableOpenAiModels = [];
        Save();
        NotifyChanged();
        Credentials.Forget(CredentialOrigin);
        Preferences.SetString(OpenAiListedKey, "");
    }

    public static void DisconnectGoogle()
    {
        googleApiKey = "";
        availableGoogleModels = [];
        Save();
        NotifyChanged();
        Credentials.Forget(GoogleCredentialOrigin);
        Preferences.SetString(GoogleListedKey, "");
    }

    public static void DisconnectXai()
    {
        xaiApiKey = "";
        availableXaiModels = [];
        Save();
        NotifyChanged();
        Credentials.Forget(XaiCredentialOrigin);
        Preferences.SetString(XaiListedKey, "");
    }

    public static void DisconnectBfl()
    {
        bflApiKey = "";
        availableBflModels = [];
        Save();
        NotifyChanged();
        Credentials.Forget(BflCredentialOrigin);
        Preferences.SetString(BflListedKey, "");
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

    public static void ConfigureServer(string address)
    {
        ServerAddress = address;
        Preferences.SetString("Server.Address", address);
        ServerConnected = false;
        ServerModel = "";
        NotifyChanged();
    }

    public static void SetServerToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            try { Credentials.Forget(ServerCredentialOrigin); } catch (Exception ex) when (StoreUnavailable(ex)) { }
            serverToken = "";
        }
        else
        {
            Credentials.Set(ServerCredentialOrigin, token);
            serverToken = token;
        }
    }

    // Entering the token is the consent to use it (ADR 0032): no extra
    // confirmation exists, and forgetting removes it everywhere. A store
    // failure keeps the token out of memory too.
    public static void SetHuggingFaceToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            try { Credentials.Forget(HuggingFaceCredentialOrigin); } catch (Exception ex) when (StoreUnavailable(ex)) { }
            huggingFaceToken = "";
        }
        else
        {
            try { Credentials.Set(HuggingFaceCredentialOrigin, token); }
            catch (Exception ex) when (StoreUnavailable(ex)) { throw new InvalidOperationException("This computer's credential store is unavailable. The token was not saved."); }
            huggingFaceToken = token;
        }
        NotifyChanged();
    }

    public static void SetServerChecked(string modelName)
    {
        ServerConnected = modelName.Length != 0;
        ServerModel = modelName;
        NotifyChanged();
    }

    public static void DisconnectServer()
    {
        ServerAddress = "";
        serverToken = "";
        ServerConnected = false;
        ServerModel = "";
        Preferences.SetString("Server.Address", "");
        Credentials.Forget(ServerCredentialOrigin);
        NotifyChanged();
    }

    private static string Connect(ref string current, string origin, string key)
    {
        // The key is stored, not remembered: saving is the default and a
        // store failure stops the connection instead of a plaintext copy.
        Credentials.Set(origin, key);
        current = key;
        Save();
        NotifyChanged();
        return "Connected. API key saved in this computer's credential store.";
    }

    private static string ReadKey(string origin)
    {
        try
        { return Credentials.Read(origin) ?? ""; }
        catch (Exception ex) when (StoreUnavailable(ex)) { return ""; }
    }

    private static void NotifyChanged()
    {
        Revision++;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void Save() => Preferences.SetBool("OpenAI.IncludeNames", IncludeNames);

    private const string OpenAiListedKey = "OpenAI.ListedModels";
    private const string GoogleListedKey = "Google.ListedModels";
    private const string XaiListedKey = "Xai.ListedModels";
    private const string BflListedKey = "Bfl.ListedModels";

    private static IReadOnlyList<ModelCapabilities> ReadListedModels(string key)
    {
        try
        {
            string json = Preferences.GetString(key, "") ?? "";
            if (json.Length == 0) return [];
            return JsonSerializer.Deserialize<ModelCapabilities[]>(json, WireJson.Options) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // A stale cache is better lost than fatal: the menu falls back to
            // the compiled rows until the next refresh.
            return [];
        }
    }

    private static void WriteListedModels(string key, IReadOnlyList<ModelCapabilities> models)
    {
        try { Preferences.SetString(key, JsonSerializer.Serialize(models, WireJson.Options)); }
        catch (Exception ex) when (ex is JsonException or ArgumentException || StoreUnavailable(ex)) { }
    }

    internal static bool StoreUnavailable(Exception ex) => ex is InvalidOperationException or NotSupportedException or DllNotFoundException or EntryPointNotFoundException or TypeInitializationException;
}

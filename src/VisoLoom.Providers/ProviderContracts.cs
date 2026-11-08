using VisoLoom.Core;

namespace VisoLoom.Providers;

public sealed record ProviderProgress(string Stage, Guid? JobId = null);
public sealed record ProviderResult(byte[] PngBytes, RenderResult Metadata)
{
    public byte[]? NativePngBytes { get; init; }
}
public sealed record ProviderDescriptor(string Id, string DisplayName, bool Available, string Status);
public static class ProviderCatalogue
{
    public static IReadOnlyList<ProviderDescriptor> All { get; } = [
        new("local", "Local reference server", true, "Unchanged reference only"),
        new("bfl", "Black Forest Labs", false, "Not implemented"),
        new("openai", "OpenAI", true, "Requires an API key and cloud opt-in"),
        new("google", "Google", false, "Adapter scaffolded; size contract and live acceptance pending"),
        new("xai", "xAI", false, "Adapter scaffolded; size contract and live acceptance pending")
    ];
}

public interface IRenderProvider : IDisposable
{
    Task<ProviderCapabilities> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<ProviderResult> RenderAsync(
        SceneManifest scene,
        IReadOnlyDictionary<string, byte[]> assets,
        RenderIntent intent,
        ResolutionPlan plan,
        IProgress<ProviderProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

// Encoded images cross this boundary; implementations use their host's image codec.
public interface IImageProcessor
{
    byte[] PrepareReference(byte[] png, ResolutionPlan plan);
    byte[] NormalizeResult(byte[] png, ResolutionPlan plan);
}

// Credentials never enter scene records or ordinary settings.
public interface ICredentialStore
{
    string? Read(string origin);
    void Set(string origin, string token);
    void Forget(string origin);
}

public sealed class SessionCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> tokens = new(StringComparer.Ordinal);
    public string? Read(string origin) => tokens.GetValueOrDefault(origin);
    public void Set(string origin, string token) => tokens[origin] = token;
    public void Forget(string origin) => tokens.Remove(origin);
}

public sealed class ProviderException(string code, string safeMessage, Guid? jobId = null) : Exception(safeMessage)
{
    public string Code { get; } = code;
    public Guid? JobId { get; } = jobId;
}

namespace VisoLoom.ReferenceServer;
/// <summary>Limits for the loopback development service; no inference engine runs here.</summary>
public sealed record ReferenceServerOptions
{
    public string StorageRoot { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisoLoom", "reference-server");
    public string BearerToken { get; init; } = "";
    public int Port { get; init; } = 5179;
    public string? DevelopmentPipeName { get; init; }
    public ILocalEngine? LocalEngine { get; init; }
    public long MaximumRequestBytes { get; init; } = 36L * 1024 * 1024;
    public int MaximumJsonBytes { get; init; } = 1024 * 1024;
    public long MaximumImageBytes { get; init; } = 32L * 1024 * 1024;
    public long MaximumPixels { get; init; } = 16_777_216;
    public int MaximumEdge { get; init; } = 8192;
    public int QueueCapacity { get; init; } = 4;
    public TimeSpan WorkerInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan Retention { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan ExpiredIdRetention { get; init; } = TimeSpan.FromHours(24);

    public static ReferenceServerOptions FromEnvironment() => new()
    {
        BearerToken = Environment.GetEnvironmentVariable("VISOLOOM_REFERENCE_TOKEN") ?? "",
        LocalEngine = ManagedEngine.FromConfiguration(Environment.GetEnvironmentVariable("VISOLOOM_ENGINE_CONFIG")),
        StorageRoot = Environment.GetEnvironmentVariable("VISOLOOM_REFERENCE_STORAGE") ?? new ReferenceServerOptions().StorageRoot,
        DevelopmentPipeName = Environment.GetEnvironmentVariable("VISOLOOM_REFERENCE_AUTOCONNECT") == "1"
            ? VisoLoom.Protocol.LocalDevelopmentHandshake.PipeName : null
    };
    public void Validate()
    {
        if (BearerToken.Length < 32 || BearerToken.Any(char.IsControl))
            throw new InvalidOperationException("Set VISOLOOM_REFERENCE_TOKEN to a cryptographically random token of at least 32 characters. The token is session-only and is never logged or saved.");
        if (Port is < 1 or > 65535 || MaximumRequestBytes <= 0 || MaximumJsonBytes <= 0 || MaximumImageBytes <= 0 || MaximumPixels <= 0 || MaximumEdge <= 0 || QueueCapacity <= 0 || WorkerInterval <= TimeSpan.Zero || Retention <= TimeSpan.Zero || ExpiredIdRetention <= TimeSpan.Zero)
            throw new InvalidOperationException("Reference server limits must be positive and its port must be valid.");
        if (!Path.IsPathFullyQualified(StorageRoot))
            throw new InvalidOperationException("The reference storage root must be an absolute directory.");
        if (DevelopmentPipeName is not null)
        {
            VisoLoom.Protocol.LocalDevelopmentHandshake.ValidatePipeName(DevelopmentPipeName);
            Array.Clear(VisoLoom.Protocol.LocalDevelopmentHandshake.Encode(Port, BearerToken));
        }
    }
}

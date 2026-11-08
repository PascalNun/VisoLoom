using VisoLoom.Core;

namespace VisoLoom.Providers;

/// <summary>FLUX.2 pro editing scaffold. Rendering stays fail-closed until endpoint research resolves the reference-upload and size contracts.</summary>
public sealed class BflImageProvider : IRenderProvider
{
    public const string ModelId = "flux-2-pro";
    public const string CapabilityRevision = "bfl-flux-2-pro-2026-10-12";
    private readonly HttpClient client;
    private readonly ResolutionConstraints resolution;

    // The documented 4 MP family claim and width/height minimum are not pinned to verified edit pixel tables yet.
    public static ResolutionConstraints UnpinnedResolution => new() { IsKnown = false, MinEdge = 0, MaxEdge = 0, MinPixels = 0, MaxPixels = 0 };

    public static ModelCapabilities UnpinnedModel => new()
    {
        Id = ModelId,
        Version = ModelId,
        DisplayName = "BFL · FLUX.2 pro (image editing)",
        SupportsPrompt = true,
        SupportsPadding = true,
        SupportsResampling = true,
        // No confirmed remote cancellation exists for the pro editing endpoint; use client-only stop-waiting semantics.
        SupportsCancellation = false,
        Resolution = UnpinnedResolution
    };

    public BflImageProvider(string apiKey, bool cloudAllowed, HttpMessageHandler? handler = null,
        ResolutionConstraints? pinnedResolution = null)
    {
        if (!cloudAllowed)
            throw new ArgumentException("Enable this BFL cloud connection in VisoLoom Settings before sending an image.");
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 4096 || apiKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("Enter a valid BFL API key in VisoLoom Settings.");
        resolution = pinnedResolution ?? UnpinnedResolution;
        client = handler is null
            ? new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) : new(handler);
        client.BaseAddress = new("https://api.bfl.ai/");
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Add("x-key", apiKey);
    }

    // BFL documents no model-list surface, so discovery returns the pinned capabilities without a request or credential use.
    public Task<ProviderCapabilities> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ProviderCapabilities
        {
            ProviderId = "bfl",
            CapabilityRevision = CapabilityRevision,
            Models = [Capabilities],
            MaximumImageBytes = null,
            MaximumDecodedPixels = resolution.IsKnown ? resolution.MaxPixels : null
        });
    }

    private ModelCapabilities Capabilities => new()
    {
        Id = ModelId,
        Version = ModelId,
        DisplayName = UnpinnedModel.DisplayName,
        SupportsPrompt = true,
        SupportsPadding = true,
        SupportsResampling = true,
        SupportsCancellation = false,
        Resolution = resolution
    };

    public Task<ProviderResult> RenderAsync(SceneManifest scene, IReadOnlyDictionary<string, byte[]> assets,
        RenderIntent intent, ResolutionPlan plan, IProgress<ProviderProgress>? progress = null, CancellationToken cancellationToken = default)
        => RenderCoreAsync(scene, assets, intent, plan, progress, cancellationToken);

    public Task<ProviderResult> RenderPreparedAsync(SceneManifest scene, IReadOnlyDictionary<string, byte[]> assets,
        RenderIntent intent, ResolutionPlan plan, byte[] preparedReference,
        IProgress<ProviderProgress>? progress = null, CancellationToken cancellationToken = default)
        => RenderCoreAsync(scene, assets, intent, plan, progress, cancellationToken);

    private Task<ProviderResult> RenderCoreAsync(SceneManifest scene, IReadOnlyDictionary<string, byte[]> assets,
        RenderIntent intent, ResolutionPlan plan, IProgress<ProviderProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SceneValidator.Validate(scene).Count != 0 || assets.Count != 1 || !assets.TryGetValue("reference.png", out _))
            throw new ArgumentException("A valid scene and one reference PNG are required.");
        if (!resolution.IsKnown)
            throw new ProviderException("sizeContractUnpinned", "BFL's documented output limits are not pinned to verified edit pixel sizes yet. No image was sent. Complete the endpoint size research before rendering.");
        // The pro schema documents image-URL inputs; inline base64 acceptance is unresolved and no VisoLoom image host exists.
        throw new ProviderException("referenceUploadUnresolved", "BFL documents hosted image-URL references, and inline base64 acceptance is unresolved. No image was sent and no reference host was contacted. Resolve the reference-upload contract in research before rendering.");
    }

    public void Dispose() => client.Dispose();
}

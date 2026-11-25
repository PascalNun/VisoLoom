using System.Net;
using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

/// <summary>FLUX.2 pro editing adapter. BFL documents no model list, so the curated, documented models are selectable; rendering stays fail-closed on the reference-upload contract the owner has not resolved (ADR 0021/0027) — no image is ever sent and no reference host is contacted.</summary>
public sealed class BflImageProvider : IRenderProvider
{
    // The pinned model BFL recommends as its default, and the moving preview
    // endpoint the documentation names alongside it: the curated set carries
    // the forward-compatibility an account list would.
    public const string ModelId = "flux-2-pro";
    public const string PreviewModelId = "flux-2-pro-preview";
    public const string CapabilityRevision = "bfl-flux-2-pro-2026-11-20";
    public static Uri Origin { get; } = new("https://api.bfl.ai/");

    // The documented pro contract: width and height of at least 64 pixels, a
    // family claim of up to 4 megapixels of output. No maximum edge or pixel
    // grid is published, so the 16-pixel alignment and the 16,384-pixel
    // planning bound are VisoLoom's own choices, not API claims.
    public static ResolutionConstraints DocumentedResolution { get; } = new()
    {
        IsKnown = true,
        MinEdge = 64,
        MaxEdge = 16384,
        MinPixels = 4_096,
        MaxPixels = 4_194_304,
        DimensionMultiple = 16,
        AllowUpscaling = true,
        AllowFractionalSampling = true
    };

    public static bool IsCuratedModel(string? id) => id is ModelId or PreviewModelId;

    /// <summary>Capabilities for the documented model pair; no account-list variation exists for BFL.</summary>
    public static ModelCapabilities AccountModel(string id)
    {
        if (!IsCuratedModel(id))
            throw new ArgumentException("Only the documented FLUX.2 pro models are selectable.");
        return new()
        {
            Id = id,
            Version = id,
            DisplayName = id == ModelId ? "BFL · FLUX.2 pro" : "BFL · FLUX.2 pro preview",
            SupportsPrompt = true,
            SupportsPadding = true,
            SupportsResampling = true,
            // No confirmed remote cancellation for the pro editing endpoint; client-only stop-waiting semantics.
            SupportsCancellation = false,
            Resolution = DocumentedResolution
        };
    }

    public BflImageProvider(string apiKey, bool cloudAllowed, HttpMessageHandler? handler = null,
        string modelId = ModelId)
    {
        if (!cloudAllowed)
            throw new ArgumentException("Enable this BFL cloud connection in VisoLoom Settings before sending an image.");
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 4096 || apiKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("Enter a valid BFL API key in VisoLoom Settings.");
        if (!IsCuratedModel(modelId))
            throw new ArgumentException("Only the documented FLUX.2 pro models are selectable.");
        capabilities = AccountModel(modelId);
        client = handler is null
            ? new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) : new(handler);
        client.BaseAddress = Origin;
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Add("x-key", apiKey);
    }

    private readonly HttpClient client;
    private readonly ModelCapabilities capabilities;

    // BFL documents no model-list surface, but it documents a per-key credits
    // check: the connection check validates the key without an image, and the
    // documented curated pair is what the Model menu offers (ADR 0027).
    public async Task<ProviderCapabilities> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await client.GetAsync("v1/credits", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            RequireSuccess(response);
            var body = await ReadJson(response, 8192, cancellationToken).ConfigureAwait(false);
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("credits", out var credits) ||
                credits.ValueKind != JsonValueKind.Number)
                throw new ProviderException("invalidResponse", "BFL returned an invalid credits response. No image was sent.");
            return new()
            {
                ProviderId = "bfl",
                CapabilityRevision = CapabilityRevision,
                Models = [AccountModel(ModelId), AccountModel(PreviewModelId)],
                MaximumImageBytes = null,
                MaximumDecodedPixels = DocumentedResolution.MaxPixels
            };
        }
        catch (JsonException)
        {
            throw new ProviderException("invalidResponse", "BFL returned an invalid credits response. No image was sent.");
        }
    }

    public Task<ProviderResult> RenderAsync(SceneManifest scene, IReadOnlyDictionary<string, byte[]> assets,
        RenderIntent intent, ResolutionPlan plan, IProgress<ProviderProgress>? progress = null, CancellationToken cancellationToken = default)
        => RenderCoreAsync(scene, assets, intent, plan, cancellationToken);

    public Task<ProviderResult> RenderPreparedAsync(SceneManifest scene, IReadOnlyDictionary<string, byte[]> assets,
        RenderIntent intent, ResolutionPlan plan, byte[] preparedReference,
        IProgress<ProviderProgress>? progress = null, CancellationToken cancellationToken = default)
        => RenderCoreAsync(scene, assets, intent, plan, cancellationToken);

    private Task<ProviderResult> RenderCoreAsync(SceneManifest scene, IReadOnlyDictionary<string, byte[]> assets,
        RenderIntent intent, ResolutionPlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SceneValidator.Validate(scene).Count != 0 || assets.Count != 1 || !assets.TryGetValue("reference.png", out _))
            throw new ArgumentException("A valid scene and one reference PNG are required.");
        if (plan != ResolutionPlanner.Plan(scene.Frame, intent.RequestedOutput, capabilities.Resolution) || plan.Capture != scene.Frame)
            throw new ArgumentException("The image plan differs from the model's supported complete-frame plan.");
        // The published API documents hosted image-URL references only — no
        // upload or base64 surface exists in the complete endpoint index — and
        // VisoLoom hosts no images. How (or whether) a VisoLoom reference
        // reaches BFL is an owner decision that is not made; nothing is sent
        // and no reference host is contacted until it is (ADR 0021/0027).
        throw new ProviderException("referenceUploadUnresolved", "BFL documents hosted image-URL references, and inline base64 acceptance is unresolved. No image was sent and no reference host was contacted. Resolve the reference-upload contract in research before rendering.");
    }

    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;
        string message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "BFL rejected the API key. Check it in Settings.",
            HttpStatusCode.Forbidden => "This BFL account cannot access the image models.",
            HttpStatusCode.TooManyRequests => "BFL reported a rate or account limit. Check your credits before retrying.",
            _ => $"BFL rejected the request (HTTP {(int)response.StatusCode}). No automatic retry was made."
        };
        throw new ProviderException("bflRejected", message);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response, long maximum, CancellationToken ct)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/json" || response.Content.Headers.ContentLength > maximum)
            throw new ProviderException("invalidResponse", "BFL returned an unsupported or oversized response.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > maximum)
                throw new ProviderException("invalidResponse", "BFL returned an oversized response.");
            bytes.Write(buffer, 0, count);
        }
        return SubmissionValidator.Deserialize<JsonElement>(bytes.ToArray());
    }

    public void Dispose() => client.Dispose();
}

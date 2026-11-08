using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

/// <summary>One reference-image edit through the Gemini Interactions surface. Rendering stays fail-closed until endpoint research pins the size contract.</summary>
public sealed class GoogleImageProvider : IRenderProvider
{
    public const string ModelId = "gemini-nano-banana-2.1";
    public const string CapabilityRevision = "google-interactions-2026-10-12";
    public const long MaximumInputBytes = 12L * 1024 * 1024;
    public const long MaximumOutputBytes = 32L * 1024 * 1024;
    private readonly HttpClient client;
    private readonly IImageProcessor images;
    private readonly ResolutionConstraints resolution;

    // The documented 1K/2K/4K tiers are not pinned to verified pixel tables yet. Rendering fails closed until that research lands.
    public static ResolutionConstraints UnpinnedResolution => new() { IsKnown = false, MinEdge = 0, MaxEdge = 0, MinPixels = 0, MaxPixels = 0 };

    public static ModelCapabilities UnpinnedModel => new()
    {
        Id = ModelId,
        Version = ModelId,
        DisplayName = "Google · Nano Banana 2.1 (image editing)",
        SupportsPrompt = true,
        SupportsPadding = true,
        SupportsResampling = true,
        // Interactive cancellation is not established for this surface.
        SupportsCancellation = false,
        Resolution = UnpinnedResolution
    };

    public GoogleImageProvider(string apiKey, bool cloudAllowed, IImageProcessor images, HttpMessageHandler? handler = null,
        ResolutionConstraints? pinnedResolution = null)
    {
        if (!cloudAllowed)
            throw new ArgumentException("Enable this Google cloud connection in VisoLoom Settings before sending an image.");
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 4096 || apiKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("Enter a valid Google API key in VisoLoom Settings.");
        this.images = images;
        resolution = pinnedResolution ?? UnpinnedResolution;
        client = handler is null
            ? new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) : new(handler);
        client.BaseAddress = new("https://generativelanguage.googleapis.com/");
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
    }

    public async Task<ProviderCapabilities> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await client.GetAsync("v1beta/models/" + ModelId, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            RequireSuccess(response);
            var body = await ReadJson(response, 65_536, cancellationToken).ConfigureAwait(false);
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("name", out var name) ||
                name.ValueKind != JsonValueKind.String || name.GetString() != "models/" + ModelId)
                throw new ProviderException("modelUnavailable", "The configured Google image-editing model is not available for this account. No replacement model was selected.");
            return new()
            {
                ProviderId = "google",
                CapabilityRevision = CapabilityRevision,
                Models = [Capabilities],
                MaximumImageBytes = MaximumInputBytes,
                MaximumDecodedPixels = resolution.IsKnown ? resolution.MaxPixels : null
            };
        }
        catch (JsonException)
        {
            throw new ProviderException("invalidResponse", "Google returned an invalid model response. No image was sent.");
        }
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
        => RenderCoreAsync(scene, assets, intent, plan, null, progress, cancellationToken);

    // The native preview prepares this canvas locally. Submit those same bytes, never a new transform.
    public Task<ProviderResult> RenderPreparedAsync(SceneManifest scene, IReadOnlyDictionary<string, byte[]> assets,
        RenderIntent intent, ResolutionPlan plan, byte[] preparedReference,
        IProgress<ProviderProgress>? progress = null, CancellationToken cancellationToken = default)
        => RenderCoreAsync(scene, assets, intent, plan, preparedReference, progress, cancellationToken);

    private async Task<ProviderResult> RenderCoreAsync(SceneManifest scene, IReadOnlyDictionary<string, byte[]> assets,
        RenderIntent intent, ResolutionPlan plan, byte[]? preparedReference,
        IProgress<ProviderProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!resolution.IsKnown)
            throw new ProviderException("sizeContractUnpinned", "Google's documented image tiers are not pinned to verified pixel sizes yet. No image was sent. Complete the endpoint size research before rendering.");
        if (SceneValidator.Validate(scene).Count != 0 || assets.Count != 1 || !assets.TryGetValue("reference.png", out var source))
            throw new ArgumentException("A valid scene and one reference PNG are required.");
        var reference = scene.Passes.Single(p => p.Kind == PassKind.Reference && p.Status == PassStatus.Available);
        if (source.LongLength > MaximumOutputBytes || reference.ByteCount != source.LongLength || reference.Sha256 != ContentDigest.Sha256(source) || PngHeader.Read(source) != scene.Frame)
            throw new ArgumentException("The reference image does not match the captured scene.");
        if (plan != ResolutionPlanner.Plan(scene.Frame, intent.RequestedOutput, resolution) || plan.Capture != scene.Frame)
            throw new ArgumentException("The image plan differs from the model's supported complete-frame plan.");
        if (intent.PreservationObjective != PreservationRules.Objective || !intent.EffectivePrompt.Contains(PreservationRules.Objective, StringComparison.Ordinal) || intent.EffectivePrompt.Length > 32_000)
            throw new ArgumentException("The composed prompt must retain the geometry-preservation objective and fit the model's limit.");
        progress?.Report(new("preparing"));
        byte[] prepared;
        try
        {
            prepared = preparedReference ?? images.PrepareReference(source, plan);
        }
        catch (Exception ex) when (IsImageProcessingFailure(ex))
        {
            throw new ProviderException("imagePreparationFailed", "Rhino could not prepare the reference image for Google. No image was sent. Update the VisoLoom build and restart Rhino.");
        }
        if (prepared.LongLength > MaximumInputBytes || PngHeader.Read(prepared) != plan.Native)
            throw new ArgumentException("The prepared reference exceeds the upload limit or has unexpected dimensions.");
        cancellationToken.ThrowIfCancellationRequested();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            model = ModelId,
            input = new object[]
            {
                new { type = "text", text = intent.EffectivePrompt },
                new { type = "image", mime_type = "image/png", data = Convert.ToBase64String(prepared) }
            },
            response_format = new { type = "image", mime_type = "image/png" }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1beta/interactions") { Content = new ByteArrayContent(payload) };
        request.Content.Headers.ContentType = new("application/json");
        progress?.Report(new("generating"));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            RequireSuccess(response);
            var body = await ReadJson(response, 45L * 1024 * 1024, cancellationToken).ConfigureAwait(false);
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("output_image", out var output) ||
                output.ValueKind != JsonValueKind.Object || !output.TryGetProperty("data", out var encoded) || encoded.ValueKind != JsonValueKind.String)
                throw new ProviderException("invalidResult", "Google did not return the requested single PNG image.");
            if (output.TryGetProperty("mime_type", out var mime) && mime.ValueKind == JsonValueKind.String && mime.GetString() != "image/png")
                throw new ProviderException("invalidResult", "Google returned an image in a format other than the requested PNG.");
            byte[] png = Convert.FromBase64String(encoded.GetString()!);
            if (png.LongLength > MaximumOutputBytes)
                throw new ProviderException("invalidResult", "The generated image exceeds the byte limit.");
            progress?.Report(new("processing"));
            byte[] final;
            try
            {
                final = images.NormalizeResult(png, plan);
            }
            catch (Exception ex) when (IsImageProcessingFailure(ex))
            {
                throw new ProviderException("imageNormalizationFailed", "Google returned an image, but Rhino could not restore its frame. The request may have been charged. No automatic retry was made.");
            }
            if (final.LongLength > MaximumOutputBytes || PngHeader.Read(final) != plan.Final)
                throw new ProviderException("invalidResult", "The generated image could not be restored to the complete Rhino frame.");
            return new(final, new()
            {
                ResultId = Guid.NewGuid(),
                SceneId = scene.SceneId,
                SceneDigest = ContentDigest.Sha256(JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options)),
                ProviderId = "google",
                ModelId = ModelId,
                ModelVersion = ModelId,
                Plan = plan,
                CompletedAt = DateTimeOffset.UtcNow,
                OutputSha256 = ContentDigest.Sha256(final),
                OutputByteCount = final.LongLength,
                ConsumedPasses = [PassKind.Reference],
                NativeOutputSha256 = ContentDigest.Sha256(png),
                NativeOutputByteCount = png.LongLength,
                OmittedPasses = scene.Passes.Where(p => p.Kind != PassKind.Reference).Select(p => p.Kind).ToArray(),
                DisclosedContextFields = intent.DisclosedContextFields,
                ProcessingHistory = ["Google Interactions reference-image edit; one output; no automatic retries",
                    $"Requested canvas {plan.Native.Width}x{plan.Native.Height}; content {plan.ContentRectangle}; final {plan.Final.Width}x{plan.Final.Height}"],
                Warnings = [new("generativeGeometry", "Compare the generated image with the source. Geometry preservation is the objective, not a deterministic guarantee.")]
            }) { NativePngBytes = png };
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new ProviderException("generationUnconfirmed", "Stopped waiting for Google. The request may still finish and be charged. No automatic retry was made.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            throw new ProviderException("invalidResult", "Google returned an invalid or unsupported image response. No automatic retry was made.");
        }
        finally
        {
            Array.Clear(payload);
        }
    }

    private static bool IsImageProcessingFailure(Exception ex) => ex is
        ArgumentException or InvalidOperationException or NotSupportedException or MissingMethodException or
        TypeLoadException or System.Runtime.InteropServices.ExternalException or IOException;

    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;
        string message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Google rejected the API key. Check it in Settings.",
            HttpStatusCode.Forbidden => "This Google account cannot access the configured image model.",
            HttpStatusCode.TooManyRequests => "Google reported a rate or account limit. Check your account before retrying.",
            _ => $"Google rejected the request (HTTP {(int)response.StatusCode}). No automatic retry was made."
        };
        throw new ProviderException("googleRejected", message);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response, long maximum, CancellationToken ct)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/json" || response.Content.Headers.ContentLength > maximum)
            throw new ProviderException("invalidResponse", "Google returned an unsupported or oversized response.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > maximum)
                throw new ProviderException("invalidResponse", "Google returned an oversized response.");
            bytes.Write(buffer, 0, count);
        }
        return SubmissionValidator.Deserialize<JsonElement>(bytes.ToArray());
    }

    public void Dispose() => client.Dispose();
}

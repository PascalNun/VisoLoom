using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

/// <summary>One reference-image edit through the Gemini Interactions surface. The account's image models are the selection source (ADR 0024/0025); no aspect-ratio or size values are invented beyond the documented request.</summary>
public sealed class GoogleImageProvider : IRenderProvider
{
    public const string ModelId = "gemini-nano-banana-2.1";
    public const string CapabilityRevision = "google-interactions-2026-11-20";
    public static Uri Origin { get; } = new("https://generativelanguage.googleapis.com/");
    public const long MaximumInputBytes = 12L * 1024 * 1024;
    public const long MaximumOutputBytes = 32L * 1024 * 1024;
    private readonly HttpClient client;
    private readonly IImageProcessor images;
    private readonly ModelCapabilities capabilities;

    // Image-edit model ids in the Gemini listing carry "-image" (for example
    // gemini-3.1-flash-image) or the Nano Banana alias (gemini-nano-banana-…).
    public static bool IsImageModel(string? id)
    {
        if (id is null || id.Length < 3 || id.Length > 127 || id.Any(char.IsControl))
            return false;
        return id.Contains("-image", StringComparison.Ordinal) || id.Contains("nano-banana", StringComparison.Ordinal);
    }

    // The aspect ratios the model pages name (researched 23.11.2026): Lite
    // names ten of its fourteen, Nano Banana 2.1 and 3.1 Flash add the extremes
    // 1:4, 4:1, 1:8 and 8:1, 3 Pro keeps its ten without them. A ratio the
    // documentation does not name stays out until it is recorded; the planner
    // pads the Rhino frame inside the nearest documented ratio, and the request
    // names the same ratio.
    public static readonly (PixelSize Ratio, string RequestValue)[] DocumentedRatios =
    [
        (new(1, 1), "1:1"),
        (new(3, 2), "3:2"),
        (new(2, 3), "2:3"),
        (new(3, 4), "3:4"),
        (new(4, 3), "4:3"),
        (new(4, 5), "4:5"),
        (new(5, 4), "5:4"),
        (new(9, 16), "9:16"),
        (new(16, 9), "16:9"),
        (new(21, 9), "21:9")
    ];

    /// <summary>The request's own string for the ratio the planned canvas is locked to.</summary>
    public static string AspectRatioFor(PixelSize canvas)
    {
        return DocumentedRatios.Single(entry =>
            (long)canvas.Width * entry.Ratio.Height == (long)canvas.Height * entry.Ratio.Width).RequestValue;
    }

    // The documented planning contract for the image-edit family: the surface
    // takes no pixel sizes, so VisoLoom plans the prepared input canvas on the
    // 16-pixel grid inside the documented 1K–4K generation range, locked to one
    // of the aspect ratios the model pages name, and asks the model for that
    // same ratio in response_format. The image_size strings differ between the
    // general guide (1K, 2K, 4K) and the Lite model page (1024px), so the
    // documented default tier is requested by sending no image_size at all.
    public static ResolutionConstraints DocumentedResolution { get; } = new()
    {
        IsKnown = true,
        MinEdge = 256,
        MaxEdge = 4096,
        MinPixels = 65_536,
        MaxPixels = 16_777_216,
        DimensionMultiple = 16,
        MaximumAspectRatio = 3,
        AllowUpscaling = true,
        AllowFractionalSampling = true,
        AllowedRatios = [.. DocumentedRatios.Select(entry => entry.Ratio)]
    };

    /// <summary>Capabilities for any account-listed image model; the compiled profile names the tested snapshot.</summary>
    public static ModelCapabilities AccountModel(string id)
    {
        if (id != ModelId && !IsImageModel(id))
            throw new ArgumentException("Only image-editing Google models are selectable.");
        return new()
        {
            Id = id,
            // The listing carries no version field; the snapshot id is the version.
            Version = id,
            DisplayName = id == ModelId ? "Google · Nano Banana 2.1 (image editing)" : "Google · " + id,
            SupportsPrompt = true,
            SupportsPadding = true,
            SupportsResampling = true,
            // Interactive cancellation is not established for this surface.
            SupportsCancellation = false,
            Resolution = DocumentedResolution
        };
    }

    public GoogleImageProvider(string apiKey, bool cloudAllowed, IImageProcessor images, HttpMessageHandler? handler = null,
        string modelId = ModelId)
    {
        if (!cloudAllowed)
            throw new ArgumentException("Enable this Google cloud connection in VisoLoom Settings before sending an image.");
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 4096 || apiKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("Enter a valid Google API key in VisoLoom Settings.");
        if (modelId != ModelId && !IsImageModel(modelId))
            throw new ArgumentException("Only image-editing Google models are selectable.");
        this.images = images;
        capabilities = AccountModel(modelId);
        client = handler is null
            ? new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) : new(handler);
        client.BaseAddress = Origin;
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
    }

    public async Task<ProviderCapabilities> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // The authenticated account listing is the access evidence and the
            // selection source. It carries names, never contracts; VisoLoom's
            // plan comes from the documented family contract above.
            using var response = await client.GetAsync("v1beta/models?pageSize=1000", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await RequireSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            var body = await ReadJson(response, 1_048_576, cancellationToken).ConfigureAwait(false);
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("models", out var listed) ||
                listed.ValueKind != JsonValueKind.Array || listed.GetArrayLength() is < 1 or > 1024)
                throw new ProviderException("invalidResponse", "Google returned an invalid model list. No image was sent.");
            var available = new List<ModelCapabilities>();
            foreach (var entry in listed.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("name", out var named) ||
                    named.ValueKind != JsonValueKind.String || !named.GetString()!.StartsWith("models/", StringComparison.Ordinal))
                    continue;
                string id = named.GetString()!["models/".Length..];
                if (IsImageModel(id))
                    available.Add(AccountModel(id));
            }
            if (available.Count == 0)
                throw new ProviderException("modelUnavailable", "No image-editing Google model is available for this account. No image was sent.");
            return new()
            {
                ProviderId = "google",
                CapabilityRevision = CapabilityRevision,
                // The listing carries no creation dates; the provider's order is kept.
                Models = available,
                MaximumImageBytes = MaximumInputBytes,
                MaximumDecodedPixels = DocumentedResolution.MaxPixels
            };
        }
        catch (JsonException)
        {
            throw new ProviderException("invalidResponse", "Google returned an invalid model list. No image was sent.");
        }
    }

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
        if (SceneValidator.Validate(scene).Count != 0 || assets.Count != 1 || !assets.TryGetValue("reference.png", out var source))
            throw new ArgumentException("A valid scene and one reference PNG are required.");
        var reference = scene.Passes.Single(p => p.Kind == PassKind.Reference && p.Status == PassStatus.Available);
        if (source.LongLength > MaximumOutputBytes || reference.ByteCount != source.LongLength || reference.Sha256 != ContentDigest.Sha256(source) || PngHeader.Read(source) != scene.Frame)
            throw new ArgumentException("The reference image does not match the captured scene.");
        if (plan != ResolutionPlanner.Plan(scene.Frame, intent.RequestedOutput, capabilities.Resolution) || plan.Capture != scene.Frame)
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
            model = capabilities.Id,
            input = new object[]
            {
                new { type = "text", text = intent.EffectivePrompt },
                new { type = "image", mime_type = "image/png", data = Convert.ToBase64String(prepared) }
            },
            // The canvas is planned at the ratio this string names, so the model
            // draws the complete Rhino frame onto the frame's own aspect instead
            // of reframing it onto a canvas of its own choosing.
            response_format = new { type = "image", mime_type = "image/png", aspect_ratio = AspectRatioFor(plan.Native) }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1beta/interactions") { Content = new ByteArrayContent(payload) };
        request.Content.Headers.ContentType = new("application/json");
        progress?.Report(new("generating"));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await RequireSuccessAsync(response, cancellationToken).ConfigureAwait(false);
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
            if (final.LongLength > MaximumOutputBytes || PngHeader.ReadExport(final) != plan.Final)
                throw new ProviderException("invalidResult", "The generated image could not be restored to the complete Rhino frame.");
            return new(final, new()
            {
                ResultId = Guid.NewGuid(),
                SceneId = scene.SceneId,
                SceneDigest = ContentDigest.Sha256(JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options)),
                ProviderId = "google",
                ModelId = capabilities.Id,
                ModelVersion = capabilities.Version,
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

    // Rejections keep the provider's own response private except for one structured,
    // bounded diagnostic: the error's machine-readable status (for example
    // INVALID_ARGUMENT). Free-text bodies stay unread in messages by design.
    private static async Task RequireSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        string? status = null;
        try
        {
            // The rejection body is read only here, on the failure path; a success body is never consumed twice.
            var rejection = await BoundedRejectionAsync(response, cancellationToken).ConfigureAwait(false);
            if (rejection is not null && rejection.Value.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object && error.TryGetProperty("status", out var named) &&
                named.ValueKind == JsonValueKind.String)
            {
                string candidate = named.GetString()!;
                if (candidate.Length is > 2 and < 64 && candidate.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && char.IsUpper(candidate[0]))
                    status = candidate;
            }
        }
        catch (Exception ex) when (ex is ProviderException or JsonException)
        {
            // An unreadable rejection body stays private; the plain status line below is the honest report.
        }
        string message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Google rejected the API key. Check it in Settings.",
            HttpStatusCode.Forbidden => "This Google account cannot access the configured image model.",
            HttpStatusCode.TooManyRequests => "Google reported a rate or account limit. Check your account before retrying.",
            _ => $"Google rejected the request (HTTP {(int)response.StatusCode}{(status is null ? "" : " · " + status)}). No automatic retry was made."
        };
        throw new ProviderException("googleRejected", message);
    }

    private static async Task<JsonElement?> BoundedRejectionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/json" ||
            response.Content.Headers.ContentLength is > 8192)
            return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > 8192)
                return null;
            bytes.Write(buffer, 0, count);
        }
        try
        {
            return SubmissionValidator.Deserialize<JsonElement>(bytes.ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
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

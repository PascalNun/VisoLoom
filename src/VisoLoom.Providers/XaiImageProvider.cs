using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

/// <summary>One reference-image edit through the xAI image-edit surface. The account's image models are the selection source (ADR 0024/0026); every sent value is part of the documented request, and result URLs are never fetched.</summary>
public sealed class XaiImageProvider : IRenderProvider
{
    public const string ModelId = "grok-imagine-image-2.0";
    public const string CapabilityRevision = "xai-edits-2026-11-20";
    // The image-edit model family the account list is filtered to: Grok Imagine
    // image models (grok-imagine-image, grok-imagine-image-2.0, later releases);
    // text, video and voice models are not.
    public const string FamilyPrefix = "grok-imagine-image";
    public static Uri Origin { get; } = new("https://api.x.ai/");
    public const long MaximumInputBytes = 14L * 1024 * 1024;
    public const long MaximumOutputBytes = 32L * 1024 * 1024;
    private readonly HttpClient client;
    private readonly IImageProcessor images;
    private readonly ModelCapabilities capabilities;

    // The aspect values the editing surface documents (researched 23.11.2026):
    // the generation page's table without 'auto', which leaves the canvas to
    // the model. VisoLoom plans the prepared input canvas locked to one of
    // these ratios inside the requested 2k tier and names the same ratio in
    // the request, so the returned canvas carries the complete Rhino frame at
    // its own aspect instead of a reframing of VisoLoom's choosing.
    public static readonly (PixelSize Ratio, string RequestValue)[] DocumentedRatios =
    [
        (new(1, 1), "1:1"),
        (new(16, 9), "16:9"),
        (new(9, 16), "9:16"),
        (new(4, 3), "4:3"),
        (new(3, 4), "3:4"),
        (new(3, 2), "3:2"),
        (new(2, 3), "2:3"),
        (new(2, 1), "2:1"),
        (new(1, 2), "1:2"),
        (new(39, 18), "19.5:9"),
        (new(18, 39), "9:19.5"),
        (new(20, 9), "20:9"),
        (new(9, 20), "9:20"),
        (new(21, 9), "21:9"),
        (new(5, 2), "5:2")
    ];

    /// <summary>The request's own string for the ratio the planned canvas is locked to.</summary>
    public static string AspectRatioFor(PixelSize canvas)
    {
        return DocumentedRatios.Single(entry =>
            (long)canvas.Width * entry.Ratio.Height == (long)canvas.Height * entry.Ratio.Width).RequestValue;
    }

    // The documented request pins the output scale, not exact pixels: the
    // resolution enum names 1k, 1.5k and 2k tiers without publishing a pixel
    // table. The canvas is planned on the 16-pixel grid inside the requested
    // 2k tier, locked to a documented ratio; the model's returned canvas is
    // normalized back into the complete Rhino frame. No pixel values are
    // invented for the request.
    public static ResolutionConstraints DocumentedResolution { get; } = new()
    {
        IsKnown = true,
        MinEdge = 256,
        MaxEdge = 2048,
        MinPixels = 65_536,
        MaxPixels = 4_194_304,
        DimensionMultiple = 16,
        MaximumAspectRatio = 2.5,
        AllowUpscaling = true,
        AllowFractionalSampling = true,
        AllowedRatios = [.. DocumentedRatios.Select(entry => entry.Ratio)]
    };

    public static bool IsImageModel(string? id)
    {
        if (id is null || id.Length < 3 || id.Length > 127 || id.Any(char.IsControl))
            return false;
        return id.StartsWith(FamilyPrefix, StringComparison.Ordinal);
    }

    /// <summary>Capabilities for any account-listed image model; the compiled profile names the tested snapshot.</summary>
    public static ModelCapabilities AccountModel(string id)
    {
        if (id != ModelId && !IsImageModel(id))
            throw new ArgumentException("Only image-editing xAI models are selectable.");
        return new()
        {
            Id = id,
            // The listing carries no version field; the model id is the version.
            Version = id,
            DisplayName = id == ModelId ? "xAI · Grok Imagine 2.0 (image editing)" : "xAI · " + id,
            SupportsPrompt = true,
            SupportsPadding = true,
            SupportsResampling = true,
            // Remote image cancellation is not established for the edit surface.
            SupportsCancellation = false,
            Resolution = DocumentedResolution
        };
    }

    public XaiImageProvider(string apiKey, bool cloudAllowed, IImageProcessor images, HttpMessageHandler? handler = null,
        string modelId = ModelId)
    {
        if (!cloudAllowed)
            throw new ArgumentException("Enable this xAI cloud connection in VisoLoom Settings before sending an image.");
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 4096 || apiKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("Enter a valid xAI API key in VisoLoom Settings.");
        if (modelId != ModelId && !IsImageModel(modelId))
            throw new ArgumentException("Only image-editing xAI models are selectable.");
        this.images = images;
        capabilities = AccountModel(modelId);
        client = handler is null
            ? new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) : new(handler);
        client.BaseAddress = Origin;
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Authorization = new("Bearer", apiKey);
    }

    public async Task<ProviderCapabilities> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // The authenticated account model list is the access evidence and
            // the selection source: every image-edit family model it offers is
            // selectable, newest first. Only family models are; the list says
            // a model exists, never what it accepts.
            using var response = await client.GetAsync("v1/models", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await RequireSuccessAsync(response, null, cancellationToken).ConfigureAwait(false);
            var body = await ReadJson(response, 1_048_576, cancellationToken).ConfigureAwait(false);
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("data", out var listed) ||
                listed.ValueKind != JsonValueKind.Array || listed.GetArrayLength() is < 1 or > 1024)
                throw new ProviderException("invalidResponse", "xAI returned an invalid model list. No image was sent.");
            var available = new List<ModelCapabilities>();
            foreach (var entry in listed.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("id", out var id) ||
                    id.ValueKind != JsonValueKind.String || !IsImageModel(id.GetString()))
                    continue;
                available.Add(AccountModel(id.GetString()!));
            }
            if (available.Count == 0)
                throw new ProviderException("modelUnavailable", "No image-editing xAI model is available for this account. No image was sent.");
            var models = available
                .OrderByDescending(model => ListedCreated(listed, model.Id))
                .ThenBy(model => model.Id, StringComparer.Ordinal)
                .ToArray();
            return new()
            {
                ProviderId = "xai",
                CapabilityRevision = CapabilityRevision,
                Models = models,
                MaximumImageBytes = MaximumInputBytes,
                MaximumDecodedPixels = DocumentedResolution.MaxPixels
            };
        }
        catch (JsonException)
        {
            throw new ProviderException("invalidResponse", "xAI returned an invalid model list. No image was sent.");
        }
    }

    private static long ListedCreated(JsonElement listed, string id)
    {
        foreach (var entry in listed.EnumerateArray())
            if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("id", out var entryId) &&
                entryId.ValueKind == JsonValueKind.String && entryId.GetString() == id &&
                entry.TryGetProperty("created", out var created) && created.ValueKind == JsonValueKind.Number &&
                created.TryGetInt64(out long value))
                return value;
        return long.MinValue;
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
            throw new ProviderException("imagePreparationFailed", "Rhino could not prepare the reference image for xAI. No image was sent. Update the VisoLoom build and restart Rhino.");
        }
        if (prepared.LongLength > MaximumInputBytes || PngHeader.Read(prepared) != plan.Native)
            throw new ArgumentException("The prepared reference exceeds the upload limit or has unexpected dimensions.");
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new("generating"));
        try
        {
            // The biggest documented tier is attempted first, per the reviewed
            // posture the owner set for account-listed models (ADR 0024).
            using var request = BuildEditRequest(resolveHighTier: true, intent, plan, prepared, out byte[] payload);
            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return await ParseEditResultAsync(scene, intent, plan, response, progress, cancellationToken, resolveHighTier: true).ConfigureAwait(false);
                var rejection = await BoundedRejectionAsync(response, cancellationToken).ConfigureAwait(false);
                if (capabilities.Id != ModelId && response.StatusCode == HttpStatusCode.BadRequest &&
                    DeclaresResolutionParameter(rejection))
                {
                    // The one verified pre-generation fallback (ADR 0026): an
                    // account-listed model explicitly declined the 2k tier. The
                    // same frozen source is resubmitted once at the documented
                    // default tier; the decline is disclosed in the history.
                    progress?.Report(new("generating"));
                    using var fallbackRequest = BuildEditRequest(resolveHighTier: false, intent, plan, prepared, out byte[] fallbackPayload);
                    try
                    {
                        using var fallbackResponse = await client.SendAsync(fallbackRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                        await RequireSuccessAsync(fallbackResponse, null, cancellationToken).ConfigureAwait(false);
                        var result = await ParseEditResultAsync(scene, intent, plan, fallbackResponse, progress, cancellationToken, resolveHighTier: false).ConfigureAwait(false);
                        return result with { Metadata = result.Metadata with { ProcessingHistory = result.Metadata.ProcessingHistory
                            .Prepend("The account-listed model declined the 2k resolution tier; the same captured source was resubmitted once at the documented default tier")
                            .ToArray() } };
                    }
                    finally { Array.Clear(fallbackPayload); }
                }
                await RequireSuccessAsync(response, rejection, cancellationToken).ConfigureAwait(false);
                return await ParseEditResultAsync(scene, intent, plan, response, progress, cancellationToken, resolveHighTier: true).ConfigureAwait(false);
            }
            finally { Array.Clear(payload); }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new ProviderException("generationUnconfirmed", "Stopped waiting for xAI. The request may still finish and be charged. No automatic retry was made.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            throw new ProviderException("invalidResult", "xAI returned an invalid or unsupported image response. No automatic retry was made.");
        }
    }

    // Every field is the documented edit request: the inline PNG reference, the
    // exact recorded prompt, the inline result format, the canvas's documented
    // aspect_ratio, and — on the first attempt only — the 2k tier. quality and
    // n stay at their documented defaults (edits serve medium; one image).
    private HttpRequestMessage BuildEditRequest(bool resolveHighTier, RenderIntent intent, ResolutionPlan plan, byte[] prepared, out byte[] payload)
    {
        payload = JsonSerializer.SerializeToUtf8Bytes(new EditRequest
        {
            Model = capabilities.Id,
            Prompt = intent.EffectivePrompt,
            Image = new EditImage { Url = "data:image/png;base64," + Convert.ToBase64String(prepared) },
            ResponseFormat = "b64_json",
            Resolution = resolveHighTier ? "2k" : null,
            AspectRatio = AspectRatioFor(plan.Native)
        });
        var request = new HttpRequestMessage(HttpMethod.Post, "v1/images/edits") { Content = new ByteArrayContent(payload) };
        request.Content.Headers.ContentType = new("application/json");
        return request;
    }

    private sealed record EditRequest
    {
        [JsonPropertyName("model")] public string Model { get; init; } = "";
        [JsonPropertyName("prompt")] public string Prompt { get; init; } = "";
        [JsonPropertyName("image")] public EditImage Image { get; init; } = new();
        [JsonPropertyName("response_format")] public string ResponseFormat { get; init; } = "";
        [JsonPropertyName("resolution")]
        // The fallback omits the tier: the documented default applies, and no null is sent.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Resolution { get; init; }
        [JsonPropertyName("aspect_ratio")]
        public string? AspectRatio { get; init; }
    }

    private sealed record EditImage
    {
        [JsonPropertyName("url")] public string Url { get; init; } = "";
        [JsonPropertyName("type")] public string Type { get; init; } = "image_url";
    }

    // A rejected resolution parameter is the one deterministic pre-generation
    // signal that an account-listed model does not accept the 2k tier; every
    // other failure remains an ordinary, never-retried rejection.
    private static bool DeclaresResolutionParameter(JsonElement? rejection) =>
        rejection is { } body && body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var error) &&
        error.ValueKind == JsonValueKind.Object && error.TryGetProperty("param", out var param) &&
        param.ValueKind == JsonValueKind.String && param.GetString() == "resolution";

    // Rejections keep the provider's own response private except for one structured,
    // bounded diagnostic: the error's machine-readable type (for example
    // invalid_request_error). Free-text bodies stay unread in messages by design.
    private static async Task RequireSuccessAsync(HttpResponseMessage response, JsonElement? knownRejection, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        // The rejection body is read only here, on the failure path; a success body is never consumed twice.
        JsonElement? rejection = knownRejection ?? await BoundedRejectionAsync(response, ct).ConfigureAwait(false);
        string? type = null;
        if (rejection is { } body && body.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
            error.TryGetProperty("type", out var named) && named.ValueKind == JsonValueKind.String)
        {
            string candidate = named.GetString()!;
            if (candidate.Length is > 2 and < 64 && candidate.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && char.IsAsciiLetter(candidate[0]))
                type = candidate;
        }
        string message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "xAI rejected the API key. Check it in Settings.",
            HttpStatusCode.Forbidden => "This xAI account cannot access the configured image model.",
            HttpStatusCode.TooManyRequests => "xAI reported a rate or account limit. Check your account before retrying.",
            _ => $"xAI rejected the request (HTTP {(int)response.StatusCode}{(type is null ? "" : " · " + type)}). No automatic retry was made."
        };
        throw new ProviderException("xaiRejected", message);
    }

    /// <summary>A bounded, structured read of a rejected response's JSON body; any other body stays unread.</summary>
    private static async Task<JsonElement?> BoundedRejectionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            if (response.Content.Headers.ContentType?.MediaType != "application/json" ||
                response.Content.Headers.ContentLength is > 8192)
                return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
            {
                if (bytes.Length + count > 8192)
                    return null;
                bytes.Write(buffer, 0, count);
            }
            return SubmissionValidator.Deserialize<JsonElement>(bytes.ToArray());
        }
        catch (Exception ex) when (ex is ProviderException or JsonException)
        {
            return null;
        }
    }

    private async Task<ProviderResult> ParseEditResultAsync(SceneManifest scene, RenderIntent intent, ResolutionPlan plan,
        HttpResponseMessage response, IProgress<ProviderProgress>? progress, CancellationToken cancellationToken, bool resolveHighTier)
    {
        var body = await ReadJson(response, 45L * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        if (body.ValueKind is not JsonValueKind.Object || !body.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != 1 || data[0].ValueKind != JsonValueKind.Object)
            throw new ProviderException("invalidResult", "xAI did not return the requested single image.");
        if (data[0].TryGetProperty("b64_json", out var encoded) && encoded.ValueKind == JsonValueKind.String)
        {
            byte[] native = Convert.FromBase64String(encoded.GetString()!);
            if (native.LongLength > MaximumOutputBytes)
                throw new ProviderException("invalidResult", "The generated image exceeds the byte limit.");
            // The surface documents the result mime per image, and the default
            // edit quality serves JPEG; the returned pixels are the model's
            // own, so the host codec decodes what actually arrived.
            string? mime = data[0].TryGetProperty("mime_type", out var declared) && declared.ValueKind == JsonValueKind.String
                ? declared.GetString() : null;
            if (mime is not null && mime is not ("image/png" or "image/jpeg" or "image/webp"))
                throw new ProviderException("invalidResult", "xAI returned an image in a format the plug-in does not accept.");
            progress?.Report(new("processing"));
            byte[] final;
            try
            {
                final = images.NormalizeResult(native, plan);
            }
            catch (Exception ex) when (IsImageProcessingFailure(ex))
            {
                throw new ProviderException("imageNormalizationFailed", "xAI returned an image, but Rhino could not restore its frame. The request may have been charged. No automatic retry was made.");
            }
            if (final.LongLength > MaximumOutputBytes || PngHeader.ReadExport(final) != plan.Final)
                throw new ProviderException("invalidResult", "The generated image could not be restored to the complete Rhino frame.");
            List<string> history = [resolveHighTier
                    ? "xAI direct reference-image edit; 2k tier requested; one inline output; no automatic retries"
                    : "xAI direct reference-image edit; default resolution tier; one inline output; no automatic retries",
                $"Requested canvas {plan.Native.Width}x{plan.Native.Height}; content {plan.ContentRectangle}; final {plan.Final.Width}x{plan.Final.Height}"];
            if (mime is not null && mime != "image/png")
                history.Add("Returned image mime: " + mime);
            return new(final, new()
            {
                ResultId = Guid.NewGuid(),
                SceneId = scene.SceneId,
                SceneDigest = ContentDigest.Sha256(JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options)),
                ProviderId = "xai",
                ModelId = capabilities.Id,
                ModelVersion = capabilities.Version,
                Plan = plan,
                CompletedAt = DateTimeOffset.UtcNow,
                OutputSha256 = ContentDigest.Sha256(final),
                OutputByteCount = final.LongLength,
                ConsumedPasses = [PassKind.Reference],
                NativeOutputSha256 = ContentDigest.Sha256(native),
                NativeOutputByteCount = native.LongLength,
                OmittedPasses = scene.Passes.Where(p => p.Kind != PassKind.Reference).Select(p => p.Kind).ToArray(),
                DisclosedContextFields = intent.DisclosedContextFields,
                ProcessingHistory = history.ToArray(),
                Warnings = [new("generativeGeometry", "Compare the generated image with the source. Geometry preservation is the objective, not a deterministic guarantee.")]
            }) { NativePngBytes = native };
        }
        // URL results stay un-fetched: the inline format was requested, and the
        // hosted download contract (host, expiry, encoding) is not recorded yet.
        if (data[0].TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
            throw new ProviderException("urlResultNotSupported", "xAI returned a hosted image URL. Downloading provider-hosted results is not implemented; no image was retrieved and no retry was made.");
        throw new ProviderException("invalidResult", "xAI did not return the requested single inline image.");
    }

    private static bool IsImageProcessingFailure(Exception ex) => ex is
        ArgumentException or InvalidOperationException or NotSupportedException or MissingMethodException or
        TypeLoadException or System.Runtime.InteropServices.ExternalException or IOException;

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response, long maximum, CancellationToken ct)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/json" || response.Content.Headers.ContentLength > maximum)
            throw new ProviderException("invalidResponse", "xAI returned an unsupported or oversized response.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > maximum)
                throw new ProviderException("invalidResponse", "xAI returned an oversized response.");
            bytes.Write(buffer, 0, count);
        }
        return SubmissionValidator.Deserialize<JsonElement>(bytes.ToArray());
    }

    public void Dispose() => client.Dispose();
}

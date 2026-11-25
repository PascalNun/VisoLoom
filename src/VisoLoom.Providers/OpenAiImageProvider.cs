using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

/// <summary>One reference-image edit, one PNG result, and no automatic billable retries.</summary>
public sealed class OpenAiImageProvider : IRenderProvider
{
    public const string ModelId = "gpt-image-2.5-sunburst-2026-09-08";
    public const string DisplayName = "OpenAI · Sunburst 2.5";
    // The image-edit model family the account list is filtered to; the list
    // itself carries no request contract, so every selectable id resolves here.
    public const string FamilyPrefix = "gpt-image-";
    public static Uri Origin { get; } = new("https://api.openai.com/");
    public const long MaximumInputBytes = 14L * 1024 * 1024;
    public const long MaximumOutputBytes = 32L * 1024 * 1024;
    private const long MaximumExportBytes = 64L * 1024 * 1024;
    private readonly HttpClient client;
    private readonly IImageProcessor images;
    private readonly ModelCapabilities capabilities;

    // The reviewed custom-size contract of the compiled profile. Account-listed
    // family models attempt it first: a newcomer that still accepts it renders
    // the Rhino frame at full native size, with no coded downgrade (ADR 0024).
    public static ResolutionConstraints ReviewedResolution { get; } = new()
    {
        IsKnown = true,
        MinEdge = 16,
        MaxEdge = 3840,
        MinPixels = 655_360,
        MaxPixels = 8_294_400,
        DimensionMultiple = 16,
        MaximumAspectRatio = 3,
        AllowUpscaling = true,
        AllowFractionalSampling = true
    };

    public static ModelCapabilities Model => new()
    {
        Id = ModelId,
        Version = ModelId,
        DisplayName = DisplayName,
        SupportsPrompt = true,
        SupportsPadding = true,
        SupportsResampling = true,
        // Remote cancellation is not established for direct image edits.
        SupportsCancellation = false,
        Resolution = ReviewedResolution
    };

    public static bool IsFamilyModel(string? id) =>
        id is not null && id.StartsWith(FamilyPrefix, StringComparison.Ordinal);

    // The family's durable request: the standard canvas set every image-edit
    // model in the family has accepted (the 512 grid within these bounds is
    // exactly 1024×1024, 1024×1536 and 1536×1024), one reference, one
    // opaque-background PNG. The compiled profile stays above it with its
    // reviewed custom sizes, and an account-listed model falls back here
    // exactly once when it explicitly declines a requested canvas size.
    public static ResolutionConstraints FamilyResolution { get; } = new()
    {
        IsKnown = true,
        MinEdge = 1024,
        MaxEdge = 1536,
        MinPixels = 1_048_576,
        MaxPixels = 1_572_864,
        DimensionMultiple = 512,
        AllowUpscaling = true,
        AllowFractionalSampling = true
    };

    // Names published on the provider's own model pages, recorded with each
    // researched contract; a listed model without an entry keeps its raw id —
    // no name is invented. New entries join exactly when a page is researched.
    private static readonly IReadOnlyDictionary<string, string> RecordedNames = new Dictionary<string, string>
    {
        // Model page "GPT Image 1" (retired October 2026), recorded 25.11.2026.
        ["gpt-image-1"] = "GPT Image 1",
        // Model page "GPT Image 1.5", recorded 25.11.2026.
        ["gpt-image-1.5"] = "GPT Image 1.5",
        // Model page "GPT Image 2" (deprecated), recorded 25.11.2026.
        ["gpt-image-2"] = "GPT Image 2",
        // Model page "GPT-Image-2.5 Sunburst", recorded 25.11.2026.
        ["gpt-image-2.5-sunburst"] = "GPT-Image-2.5 Sunburst",
        // Model page "GPT-Image-2.6 Sunburst", recorded 25.11.2026.
        ["gpt-image-2.6-sunburst"] = "GPT-Image-2.6 Sunburst"
    };

    // The pages document dated snapshots of their base model; a listed
    // snapshot of a researched base keeps the base's published name plus its
    // own date from the id. Everything else stays the raw id.
    private static string RecordedDisplay(string id)
    {
        if (RecordedNames.TryGetValue(id, out var name))
            return name;
        // A dated snapshot ends in "-yyyy-MM-dd" (the date carries its own
        // dashes); the base id carries everything before that suffix.
        if (id.Length > 11 && id[^11] == '-' &&
            DateOnly.TryParseExact(id[^10..], "yyyy-MM-dd", out var date) &&
            RecordedNames.TryGetValue(id[..^11], out var baseName))
            return baseName + " · " + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return id;
    }

    /// <summary>Capabilities for any account-listed image model: the compiled profile, or the reviewed custom-size contract attempted first for every later snapshot.</summary>
    public static ModelCapabilities AccountModel(string id) => id == ModelId ? Model : new()
    {
        Id = id,
        // The account list carries no version field; the dated snapshot id is
        // the version, exactly as the compiled profile records its own.
        Version = id,
        DisplayName = "OpenAI · " + RecordedDisplay(id),
        SupportsPrompt = true,
        SupportsPadding = true,
        SupportsResampling = true,
        SupportsCancellation = false,
        Resolution = ReviewedResolution
    };

    public OpenAiImageProvider(string apiKey, bool cloudAllowed, IImageProcessor images, HttpMessageHandler? handler = null,
        string modelId = ModelId)
    {
        if (!cloudAllowed)
            throw new ArgumentException("Enable this OpenAI cloud connection in VisoLoom Settings before sending an image.");
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 4096 || apiKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("Enter a valid OpenAI API key in VisoLoom Settings.");
        if (modelId != ModelId && !IsFamilyModel(modelId))
            throw new ArgumentException("Only image-editing OpenAI models are selectable.");
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
            RequireSuccess(response);
            var body = await ReadJson(response, 1_048_576, cancellationToken).ConfigureAwait(false);
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("data", out var listed) ||
                listed.ValueKind != JsonValueKind.Array || listed.GetArrayLength() is < 1 or > 1024)
                throw new ProviderException("invalidResponse", "OpenAI returned an invalid model list. No image was sent.");
            var available = new List<ModelCapabilities>();
            foreach (var entry in listed.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("id", out var id) ||
                    id.ValueKind != JsonValueKind.String || !IsFamilyModel(id.GetString()))
                    continue;
                available.Add(AccountModel(id.GetString()!));
            }
            if (available.Count == 0)
                throw new ProviderException("modelUnavailable", "No image-editing OpenAI model is available for this account. No image was sent.");
            var models = available
                .OrderByDescending(model => ListedCreated(listed, model.Id))
                .ThenBy(model => model.Id, StringComparer.Ordinal)
                .ToArray();
            return new()
            {
                ProviderId = "openai",
                CapabilityRevision = "openai-edits-2026-10-11",
                Models = models,
                MaximumImageBytes = MaximumInputBytes,
                MaximumDecodedPixels = models.Max(model => model.Resolution.MaxPixels)
            };
        }
        catch (JsonException)
        {
            throw new ProviderException("invalidResponse", "OpenAI returned an invalid model list. No image was sent.");
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
            throw new ProviderException("imagePreparationFailed", "Rhino could not prepare the reference image for OpenAI. No image was sent. Update the VisoLoom build and restart Rhino.");
        }
        if (prepared.LongLength > MaximumInputBytes || PngHeader.Read(prepared) != plan.Native)
            throw new ArgumentException("The prepared reference exceeds the upload limit or has unexpected dimensions.");
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new("generating"));
        try
        {
            using var request = BuildEditRequest(intent, plan, prepared, out byte[] payload);
            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return await ParseEditResultAsync(scene, intent, plan, response, progress, cancellationToken).ConfigureAwait(false);
                if (capabilities.Id != ModelId && response.StatusCode == HttpStatusCode.BadRequest &&
                    await NamesSizeParameterAsync(response, cancellationToken).ConfigureAwait(false))
                {
                    // The one verified pre-generation fallback (ADR 0024): an
                    // account-listed model explicitly declined the reviewed custom
                    // canvas it was never reviewed for. The same frozen source is
                    // re-fitted onto the family standard canvas set and submitted
                    // once; the decline is disclosed in the render's history.
                    progress?.Report(new("preparing"));
                    var baseline = ResolutionPlanner.Plan(scene.Frame, intent.RequestedOutput, FamilyResolution);
                    byte[] baselinePrepared;
                    try
                    {
                        baselinePrepared = images.PrepareReference(source, baseline);
                    }
                    catch (Exception ex) when (IsImageProcessingFailure(ex))
                    {
                        throw new ProviderException("imagePreparationFailed", "Rhino could not prepare the standard canvas for OpenAI. No image was sent. Update the VisoLoom build and restart Rhino.");
                    }
                    if (baselinePrepared.LongLength > MaximumInputBytes || PngHeader.Read(baselinePrepared) != baseline.Native)
                        throw new ProviderException("invalidResponse", "OpenAI declined the requested canvas, and the standard canvas could not be prepared. No image was sent.");
                    progress?.Report(new("generating"));
                    using var fallbackRequest = BuildEditRequest(intent, baseline, baselinePrepared, out byte[] fallbackPayload);
                    try
                    {
                        using var fallbackResponse = await client.SendAsync(fallbackRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                        RequireSuccess(fallbackResponse);
                        var result = await ParseEditResultAsync(scene, intent, baseline, fallbackResponse, progress, cancellationToken).ConfigureAwait(false);
                        return result with { Metadata = result.Metadata with { ProcessingHistory = result.Metadata.ProcessingHistory
                            .Prepend($"The account-listed model declined the reviewed custom canvas {plan.Native.Width}x{plan.Native.Height}; the same captured source was fitted onto the standard canvas set and submitted once")
                            .ToArray() } };
                    }
                    finally { Array.Clear(fallbackPayload); }
                }
                RequireSuccess(response);
                return await ParseEditResultAsync(scene, intent, plan, response, progress, cancellationToken).ConfigureAwait(false);
            }
            finally { Array.Clear(payload); }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new ProviderException("generationUnconfirmed", "Stopped waiting for OpenAI. The request may still finish and be charged. No automatic retry was made.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            throw new ProviderException("invalidResult", "OpenAI returned an invalid or unsupported image response. No automatic retry was made.");
        }
    }

    private HttpRequestMessage BuildEditRequest(RenderIntent intent, ResolutionPlan plan, byte[] prepared, out byte[] payload)
    {
        payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            model = capabilities.Id,
            images = new[] { new { image_url = "data:image/png;base64," + Convert.ToBase64String(prepared) } },
            prompt = intent.EffectivePrompt,
            size = $"{plan.Native.Width}x{plan.Native.Height}",
            n = 1,
            output_format = "png",
            background = "opaque",
            quality = "medium"
        });
        var request = new HttpRequestMessage(HttpMethod.Post, "v1/images/edits") { Content = new ByteArrayContent(payload) };
        request.Content.Headers.ContentType = new("application/json");
        return request;
    }

    // A rejected size parameter is the one deterministic pre-generation signal
    // that an account-listed model does not accept the attempted canvas; every
    // other failure remains an ordinary, never-retried rejection.
    private static async Task<bool> NamesSizeParameterAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await ReadJson(response, 8192, ct).ConfigureAwait(false);
            return body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object && error.TryGetProperty("param", out var param) &&
                param.ValueKind == JsonValueKind.String && param.GetString() == "size";
        }
        catch (Exception ex) when (ex is ProviderException or JsonException)
        {
            return false;
        }
    }

    private async Task<ProviderResult> ParseEditResultAsync(SceneManifest scene, RenderIntent intent, ResolutionPlan plan,
        HttpResponseMessage response, IProgress<ProviderProgress>? progress, CancellationToken cancellationToken)
    {
        var body = await ReadJson(response, 45L * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != 1 || data[0].ValueKind != JsonValueKind.Object ||
            !data[0].TryGetProperty("b64_json", out var encoded) || encoded.ValueKind != JsonValueKind.String)
            throw new ProviderException("invalidResult", "OpenAI did not return the requested single PNG image.");
        byte[] png = Convert.FromBase64String(encoded.GetString()!);
        if (png.LongLength > MaximumOutputBytes || PngHeader.Read(png) != plan.Native)
            throw new ProviderException("invalidResult", "The generated image has unexpected dimensions or exceeds the byte limit.");
        progress?.Report(new("processing"));
        byte[] final;
        try
        {
            final = images.NormalizeResult(png, plan);
        }
        catch (Exception ex) when (IsImageProcessingFailure(ex))
        {
            throw new ProviderException("imageNormalizationFailed", "OpenAI returned an image, but Rhino could not restore its frame. The request may have been charged. No automatic retry was made.");
        }
        if (final.LongLength > MaximumExportBytes || PngHeader.ReadExport(final) != plan.Final)
            throw new ProviderException("invalidResult", "The generated image could not be restored to the complete Rhino frame.");
        return new(final, new()
        {
            ResultId = Guid.NewGuid(),
            SceneId = scene.SceneId,
            SceneDigest = ContentDigest.Sha256(JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options)),
            ProviderId = "openai",
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
            ProcessingHistory = ["OpenAI direct reference-image edit; medium quality; one output; no automatic retries",
                $"Model canvas {plan.Native.Width}x{plan.Native.Height}; content {plan.ContentRectangle}; final {plan.Final.Width}x{plan.Final.Height}"],
            Warnings = [new("generativeGeometry", "Compare the generated image with the source. Geometry preservation is the objective, not a deterministic guarantee.")]
        })
        { NativePngBytes = png };
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
            HttpStatusCode.Unauthorized => "OpenAI rejected the API key. Check it in Settings.",
            HttpStatusCode.Forbidden => "This OpenAI account cannot access the configured image model.",
            HttpStatusCode.TooManyRequests => "OpenAI reported a rate or account limit. Check your account before retrying.",
            _ => $"OpenAI rejected the request (HTTP {(int)response.StatusCode}). No automatic retry was made."
        };
        throw new ProviderException("openaiRejected", message);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response, long maximum, CancellationToken ct)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/json" || response.Content.Headers.ContentLength > maximum)
            throw new ProviderException("invalidResponse", "OpenAI returned an unsupported or oversized response.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > maximum)
                throw new ProviderException("invalidResponse", "OpenAI returned an oversized response.");
            bytes.Write(buffer, 0, count);
        }
        return SubmissionValidator.Deserialize<JsonElement>(bytes.ToArray());
    }

    public void Dispose() => client.Dispose();
}

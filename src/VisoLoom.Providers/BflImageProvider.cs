using System.Net;
using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

/// <summary>Reference-image edits through BFL's FLUX 3 endpoints. Both published request schemas document inline base64 in the images field (recorded 25.11.2026), so the prepared reference travels in the request itself and no reference host exists in the flow; the async submit, poll and signed delivery download follow the documented integration contract.</summary>
public sealed class BflImageProvider : IRenderProvider
{
    // The two documented FLUX 3 profiles whose schemas accept inline base64
    // references: klein, the open-weights editing model, and the flagship
    // FLUX 3 Image. The FLUX.2 family below stays recorded but out of
    // Render: its schema documents hosted image-URL references only and BFL
    // publishes no upload surface, so the inline contract ADR 0021 requires
    // remains unverified for it.
    public const string ModelId = "flux-3-image-klein";
    public const string FlagshipModelId = "flux-3-image";
    public const string ProModelId = "flux-2-pro";
    public const string PreviewModelId = "flux-2-pro-preview";
    public const string CapabilityRevision = "bfl-flux-3-2026-11-25";
    public static Uri Origin { get; } = new("https://api.bfl.ai/");

    // The documented klein contract: exact width and height, each within
    // [64, 6256] and together at most 16,822,272 pixels. The flagship keeps
    // the same edges from 256 and, with reference images, documents the
    // family's 4 MP cap. No pixel grid is published, so the 16-pixel
    // alignment stays VisoLoom's own planning choice. BFL documents no
    // upload byte ceiling; the 24 MiB bound on the encoded reference is
    // VisoLoom's own sanity limit, as is the 32 MiB bound on the returned
    // image.
    public static ResolutionConstraints KleinResolution { get; } = new()
    {
        IsKnown = true,
        MinEdge = 64,
        MaxEdge = 6256,
        MinPixels = 4_096,
        MaxPixels = 16_822_272,
        DimensionMultiple = 16,
        AllowUpscaling = true,
        AllowFractionalSampling = true
    };
    public static ResolutionConstraints FlagshipResolution { get; } = new()
    {
        IsKnown = true,
        MinEdge = 256,
        MaxEdge = 6256,
        MinPixels = 65_536,
        MaxPixels = 4_194_304,
        DimensionMultiple = 16,
        AllowUpscaling = true,
        AllowFractionalSampling = true
    };
    public const long MaximumInputBytes = 24L * 1024 * 1024;
    public const long MaximumOutputBytes = 32L * 1024 * 1024;

    public static bool IsCuratedModel(string? id) => id is ModelId or FlagshipModelId;

    /// <summary>Capabilities for the two documented FLUX 3 profiles; the FLUX.2 family stays unverified and unselectable (ADR 0021/0027).</summary>
    public static ModelCapabilities AccountModel(string id)
    {
        if (!IsCuratedModel(id))
            throw new ArgumentException("Only the documented FLUX 3 models are selectable.");
        return new()
        {
            Id = id,
            Version = id,
            DisplayName = id == ModelId ? "BFL · FLUX 3 Image Klein" : "BFL · FLUX 3 Image",
            SupportsPrompt = true,
            SupportsPadding = true,
            SupportsResampling = true,
            // No confirmed remote cancellation; client-only stop-waiting semantics,
            // exactly like the other cloud adapters.
            SupportsCancellation = false,
            Resolution = id == ModelId ? KleinResolution : FlagshipResolution
        };
    }

    public BflImageProvider(string apiKey, bool cloudAllowed, IImageProcessor images, HttpMessageHandler? handler = null,
        string modelId = ModelId)
    {
        if (!cloudAllowed)
            throw new ArgumentException("Enable this BFL cloud connection in VisoLoom Settings before sending an image.");
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 4096 || apiKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("Enter a valid BFL API key in VisoLoom Settings.");
        if (!IsCuratedModel(modelId))
            throw new ArgumentException("Only the documented FLUX 3 Image Klein model is selectable.");
        this.images = images;
        capabilities = AccountModel(modelId);
        client = handler is null
            ? new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) : new(handler);
        client.BaseAddress = Origin;
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Add("x-key", apiKey);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        // The delivery download carries the signed sample URL, so the API key
        // must never ride along: a second client with the same transport rules
        // and no default headers fetches the image bytes.
        delivery = handler is null
            ? new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) : new(handler);
        delivery.Timeout = TimeSpan.FromMinutes(5);
    }

    private readonly HttpClient client;
    private readonly HttpClient delivery;
    private readonly IImageProcessor images;
    private readonly ModelCapabilities capabilities;

    // BFL documents no model-list surface, but it documents a per-key credits
    // check: the connection check validates the key without an image, and the
    // documented klein profile is what the Model menu offers (ADR 0027).
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
                Models = [AccountModel(ModelId), AccountModel(FlagshipModelId)],
                MaximumImageBytes = null,
                MaximumDecodedPixels = KleinResolution.MaxPixels
            };
        }
        catch (JsonException)
        {
            throw new ProviderException("invalidResponse", "BFL returned an invalid credits response. No image was sent.");
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
            throw new ProviderException("imagePreparationFailed", "Rhino could not prepare the reference image for BFL. No image was sent. Update the VisoLoom build and restart Rhino.");
        }
        if (prepared.LongLength > MaximumInputBytes || PngHeader.Read(prepared) != plan.Native)
            throw new ArgumentException("The prepared reference exceeds the upload limit or has unexpected dimensions.");
        cancellationToken.ThrowIfCancellationRequested();
        // The documented FLUX 3 request: the composed prompt, the prepared
        // reference as inline base64 in the first images slot, and the exact
        // planned canvas in width and height. Klein additionally documents
        // output_format and takes the recorded PNG contract; the flagship
        // schema has no output_format field, so none is sent and the delivery
        // content type decides the decode. Steps and safety tolerance are
        // left to the server's documented defaults; no undocumented field
        // is sent.
        bool klein = capabilities.Id == ModelId;
        string inline = Convert.ToBase64String(prepared);
        byte[] payload = klein
            ? JsonSerializer.SerializeToUtf8Bytes(new
            {
                prompt = intent.EffectivePrompt,
                images = new[] { inline },
                width = plan.Native.Width,
                height = plan.Native.Height,
                output_format = "png"
            })
            : JsonSerializer.SerializeToUtf8Bytes(new
            {
                prompt = intent.EffectivePrompt,
                images = new[] { inline },
                width = plan.Native.Width,
                height = plan.Native.Height
            });
        try
        {
            using var submit = new HttpRequestMessage(HttpMethod.Post, "v1/" + capabilities.Id) { Content = new ByteArrayContent(payload) };
            submit.Content.Headers.ContentType = new("application/json");
            progress?.Report(new("queued"));
            using var submitted = await client.SendAsync(submit, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            RequireSuccess(submitted);
            var acknowledged = await ReadJson(submitted, 8192, cancellationToken).ConfigureAwait(false);
            if (acknowledged.ValueKind != JsonValueKind.Object || !acknowledged.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                !acknowledged.TryGetProperty("polling_url", out var polled) || polled.ValueKind != JsonValueKind.String)
                throw new ProviderException("invalidResult", "BFL did not acknowledge the render task.") { ResponseShapeDiagnostics = BoundedShape(acknowledged) };
            // The documented polling surface: the response's own polling_url is
            // the only poll address, and BFL states it "points at the region
            // that holds your task" — so the host may be any regional BFL host,
            // not just the three named API endpoints. The key is attached only
            // after the address stays on BFL's own domain over HTTPS.
            if (!Uri.TryCreate(polled.GetString(), UriKind.Absolute, out var polling) || polling.Scheme != "https" ||
                !polling.Host.EndsWith(".bfl.ai", StringComparison.Ordinal))
                throw new ProviderException("invalidResponse", "BFL returned a polling address outside its documented API. No image was sent.");
            var finished = await PollUntilReadyAsync(polling, progress, cancellationToken).ConfigureAwait(false);
            // The guidelines document delivery URLs under delivery.*.bfl.ai with
            // a region that may change, so only the documented host pattern is
            // accepted, and the download carries no API key.
            if (!finished.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object ||
                !result.TryGetProperty("sample", out var sample) || sample.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(sample.GetString(), UriKind.Absolute, out var image) || image.Scheme != "https" ||
                !image.Host.StartsWith("delivery.", StringComparison.Ordinal) || !image.Host.EndsWith(".bfl.ai", StringComparison.Ordinal))
                throw new ProviderException("invalidResult", "BFL did not return a delivery link for the generated image.") { ResponseShapeDiagnostics = BoundedShape(finished) };
            progress?.Report(new("downloading"));
            using var downloaded = await delivery.GetAsync(image, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (downloaded.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                throw new ProviderException("invalidResult", "BFL's generated image link is no longer available. No automatic retry was made.");
            RequireSuccess(downloaded);
            if (downloaded.Content.Headers.ContentType?.MediaType is not { } media || !media.StartsWith("image/", StringComparison.Ordinal))
                throw new ProviderException("invalidResult", "BFL's delivery link did not return an image.");
            byte[] native = await ReadBoundedBytes(downloaded, MaximumOutputBytes, cancellationToken).ConfigureAwait(false);
            progress?.Report(new("processing"));
            byte[] final;
            try
            {
                // The native bytes are the requested PNG; the image processor
                // restores the complete Rhino frame from the padded canvas.
                final = images.NormalizeResult(native, plan);
            }
            catch (Exception ex) when (IsImageProcessingFailure(ex))
            {
                throw new ProviderException("imageNormalizationFailed", "BFL returned an image, but Rhino could not restore its frame. The request may have been charged. No automatic retry was made.");
            }
            if (final.LongLength > MaximumOutputBytes || PngHeader.ReadExport(final) != plan.Final)
                throw new ProviderException("invalidResult", "The generated image could not be restored to the complete Rhino frame.");
            return new(final, new()
            {
                ResultId = Guid.NewGuid(),
                SceneId = scene.SceneId,
                SceneDigest = ContentDigest.Sha256(JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options)),
                ProviderId = "bfl",
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
                ProcessingHistory = ["BFL FLUX 3 Image Klein reference-image edit; one output; no automatic retries",
                    $"Requested canvas {plan.Native.Width}x{plan.Native.Height}; content {plan.ContentRectangle}; final {plan.Final.Width}x{plan.Final.Height}"],
                Warnings = [new("generativeGeometry", "Compare the generated image with the source. Geometry preservation is the objective, not a deterministic guarantee.")]
            }) { NativePngBytes = native };
        }
        catch (ProviderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Stop-waiting, a lost submission or a lost poll leaves the task's
            // remote fate unknown; the request may still finish and be
            // charged, and no second submission is ever made.
            throw new ProviderException("generationUnconfirmed", "Stopped waiting for BFL. The request may still finish and be charged. No automatic retry was made.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            throw new ProviderException("invalidResult", "BFL returned an invalid or unsupported image response. No automatic retry was made.");
        }
        finally
        {
            Array.Clear(payload);
        }
    }

    // The documented poll loop: one request per second at most, the task's
    // own status vocabulary, and a waiting bound of VisoLoom's own choosing —
    // the guidelines document that a generated image expires minutes after it
    // is ready, so the download follows the first Ready immediately.
    private async Task<JsonElement> PollUntilReadyAsync(Uri polling, IProgress<ProviderProgress>? progress, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(10);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var poll = await client.GetAsync(polling, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            RequireSuccess(poll);
            var body = await ReadJson(poll, 65_536, cancellationToken).ConfigureAwait(false);
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
                throw new ProviderException("invalidResult", "BFL returned an invalid task status.") { ResponseShapeDiagnostics = BoundedShape(body) };
            switch (status.GetString())
            {
                case "Pending":
                    progress?.Report(new("queued"));
                    break;
                case "Reasoning":
                case "Generating":
                    // The poll reply's progress number rides the panel's
                    // 100-step bar as-is; BFL documents no unit for it.
                    progress?.Report(body.TryGetProperty("progress", out var value) && value.ValueKind == JsonValueKind.Number &&
                        value.TryGetInt32(out var step) && step >= 0
                        ? new("generating", Step: step, Steps: 100) : new("generating"));
                    break;
                case "Ready":
                    return body;
                case "Request Moderated":
                case "Content Moderated":
                    throw new ProviderException("moderated", "BFL moderated the request and returned no image. Adjust the prompt and render again.");
                case "Error":
                case "No Card Generated":
                    throw new ProviderException("bflFailed", "BFL ended the render without an image. No automatic retry was made.");
                case "Task not found":
                    throw new ProviderException("taskUnknown", "BFL no longer found the render task. No automatic retry was made.");
                default:
                    throw new ProviderException("invalidResult", "BFL returned a task status outside its documented vocabulary.") { ResponseShapeDiagnostics = BoundedShape(body) };
            }
            if (DateTimeOffset.UtcNow >= deadline)
                throw new ProviderException("generationUnconfirmed", "Stopped waiting for BFL. The request may still finish and be charged. No automatic retry was made.");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
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

    private static async Task<byte[]> ReadBoundedBytes(HttpResponseMessage response, long maximum, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > maximum)
            throw new ProviderException("invalidResult", "The generated image exceeds the byte limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > maximum)
                throw new ProviderException("invalidResult", "The generated image exceeds the byte limit.");
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }

    // An accepted reply that missed the documented shape records its structure
    // for the machine-local log; strings shrink to labels and bulk never
    // rides along, exactly like the Google adapter's record.
    private static string? BoundedShape(JsonElement body)
    {
        try
        {
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
                WriteShape(writer, body, 0);
            string shape = System.Text.Encoding.UTF8.GetString(bytes.ToArray());
            return shape.Length <= 8192 ? shape : null;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteShape(Utf8JsonWriter writer, JsonElement node, int depth)
    {
        if (depth > 16)
        {
            writer.WriteStringValue("[depth capped]");
            return;
        }
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in node.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteShape(writer, property.Value, depth + 1);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in node.EnumerateArray())
                    WriteShape(writer, item, depth + 1);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                string? text = node.GetString();
                if (text is { Length: > 64 })
                    writer.WriteStringValue(text.Substring(0, 32) + "...(" + text.Length + " chars)");
                else
                    writer.WriteStringValue(text);
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    public void Dispose()
    {
        client.Dispose();
        delivery.Dispose();
    }
}

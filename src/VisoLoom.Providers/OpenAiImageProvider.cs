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
    public const long MaximumInputBytes = 14L * 1024 * 1024;
    public const long MaximumOutputBytes = 32L * 1024 * 1024;
    private readonly HttpClient client;
    private readonly IImageProcessor images;

    public static ModelCapabilities Model => new()
    {
        Id = ModelId,
        Version = ModelId,
        DisplayName = "OpenAI · Sunburst (image editing)",
        SupportsPrompt = true,
        SupportsPadding = true,
        SupportsResampling = true,
        // Remote cancellation is not established for direct image edits.
        SupportsCancellation = false,
        Resolution = new()
        {
            IsKnown = true,
            MinEdge = 16,
            MaxEdge = 3840,
            MinPixels = 655_360,
            MaxPixels = 8_294_400,
            DimensionMultiple = 16,
            MaximumAspectRatio = 3,
            AllowUpscaling = true
        }
    };

    public OpenAiImageProvider(string apiKey, bool cloudAllowed, IImageProcessor images, HttpMessageHandler? handler = null)
    {
        if (!cloudAllowed)
            throw new ArgumentException("Enable this OpenAI cloud connection in VisoLoom Settings before sending an image.");
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 4096 || apiKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("Enter a valid OpenAI API key in VisoLoom Settings.");
        this.images = images;
        client = handler is null
            ? new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) : new(handler);
        client.BaseAddress = new("https://api.openai.com/");
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Authorization = new("Bearer", apiKey);
    }

    public async Task<ProviderCapabilities> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await client.GetAsync("v1/models/" + ModelId, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            RequireSuccess(response);
            var body = await ReadJson(response, 65_536, cancellationToken).ConfigureAwait(false);
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.String || id.GetString() != ModelId)
                throw new ProviderException("modelUnavailable", "The configured image-editing model is not available for this account. No replacement model was selected.");
            return new()
            {
                ProviderId = "openai",
                CapabilityRevision = "openai-edits-2026-10-11",
                Models = [Model],
                MaximumImageBytes = MaximumInputBytes,
                MaximumDecodedPixels = Model.Resolution.MaxPixels
            };
        }
        catch (JsonException)
        {
            throw new ProviderException("invalidResponse", "OpenAI returned an invalid model response. No image was sent.");
        }
    }

    public async Task<ProviderResult> RenderAsync(SceneManifest scene, IReadOnlyDictionary<string, byte[]> assets,
        RenderIntent intent, ResolutionPlan plan, IProgress<ProviderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SceneValidator.Validate(scene).Count != 0 || assets.Count != 1 || !assets.TryGetValue("reference.png", out var source))
            throw new ArgumentException("A valid scene and one reference PNG are required.");
        var reference = scene.Passes.Single(p => p.Kind == PassKind.Reference && p.Status == PassStatus.Available);
        if (source.LongLength > MaximumOutputBytes || reference.ByteCount != source.LongLength || reference.Sha256 != ContentDigest.Sha256(source) || PngHeader.Read(source) != scene.Frame)
            throw new ArgumentException("The reference image does not match the captured scene.");
        if (plan != ResolutionPlanner.Plan(scene.Frame, intent.RequestedOutput, Model.Resolution) || plan.Capture != scene.Frame)
            throw new ArgumentException("The image plan differs from the model's supported complete-frame plan.");
        if (intent.PreservationObjective != PreservationRules.Objective || !intent.EffectivePrompt.Contains(PreservationRules.Objective, StringComparison.Ordinal) || intent.EffectivePrompt.Length > 32_000)
            throw new ArgumentException("The composed prompt must retain the geometry-preservation objective and fit the model's limit.");
        byte[] prepared = images.PrepareReference(source, plan);
        if (prepared.LongLength > MaximumInputBytes || PngHeader.Read(prepared) != plan.Native)
            throw new ArgumentException("The prepared reference exceeds the upload limit or has unexpected dimensions.");
        cancellationToken.ThrowIfCancellationRequested();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            model = ModelId,
            images = new[] { new { image_url = "data:image/png;base64," + Convert.ToBase64String(prepared) } },
            prompt = intent.EffectivePrompt,
            size = $"{plan.Native.Width}x{plan.Native.Height}",
            n = 1,
            output_format = "png",
            background = "opaque",
            quality = "medium"
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/images/edits") { Content = new ByteArrayContent(payload) };
        request.Content.Headers.ContentType = new("application/json");
        progress?.Report(new("generating"));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            RequireSuccess(response);
            var body = await ReadJson(response, 45L * 1024 * 1024, cancellationToken).ConfigureAwait(false);
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != 1 || data[0].ValueKind != JsonValueKind.Object ||
                !data[0].TryGetProperty("b64_json", out var encoded) || encoded.ValueKind != JsonValueKind.String)
                throw new ProviderException("invalidResult", "OpenAI did not return the requested single PNG image.");
            byte[] png = Convert.FromBase64String(encoded.GetString()!);
            if (png.LongLength > MaximumOutputBytes || PngHeader.Read(png) != plan.Native)
                throw new ProviderException("invalidResult", "The generated image has unexpected dimensions or exceeds the byte limit.");
            progress?.Report(new("processing"));
            byte[] final = images.NormalizeResult(png, plan);
            if (final.LongLength > MaximumOutputBytes || PngHeader.Read(final) != plan.Final)
                throw new ProviderException("invalidResult", "The generated image could not be restored to the complete Rhino frame.");
            return new(final, new()
            {
                ResultId = Guid.NewGuid(),
                SceneId = scene.SceneId,
                SceneDigest = ContentDigest.Sha256(JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options)),
                ProviderId = "openai",
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
                ProcessingHistory = ["OpenAI direct reference-image edit; medium quality; one output; no automatic retries",
                    $"Model canvas {plan.Native.Width}x{plan.Native.Height}; content {plan.ContentRectangle}; final {plan.Final.Width}x{plan.Final.Height}"],
                Warnings = [new("generativeGeometry", "Compare the generated image with the source. Geometry preservation is the objective, not a deterministic guarantee.")]
            })
            { NativePngBytes = png };
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new ProviderException("generationUnconfirmed", "Stopped waiting for OpenAI. The request may still finish and be charged. No automatic retry was made.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            throw new ProviderException("invalidResult", "OpenAI returned an invalid or unsupported image response. No automatic retry was made.");
        }
        finally
        {
            Array.Clear(payload);
        }
    }

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

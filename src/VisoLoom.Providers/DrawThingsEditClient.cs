using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

// Calibration client for an existing local engine, not the VisoLoom server protocol.
// The HTTP API inherits app configuration; production model discovery needs a stateless engine.
public sealed class DrawThingsEditClient : IDisposable
{
    public const string ModelId = "flux_2_klein_4b_q8p.ckpt";
    public const int MaximumPromptBytes = 2048; // Experiment budget, not an exact tokenizer limit.
    public const long MaximumImageBytes = 16 * 1024 * 1024;
    private readonly HttpClient client;

    public DrawThingsEditClient(Uri endpoint, HttpMessageHandler? handler = null)
    {
        EndpointPolicy.RequireLocalEndpoint(endpoint);
        client = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
        {
            BaseAddress = endpoint,
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    public async Task CheckModelAsync(CancellationToken ct = default)
    {
        using var response = await client.GetAsync("sdapi/v1/options", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var options = await ReadJsonAsync(response, 128 * 1024, ct).ConfigureAwait(false);
        if (options.ValueKind != JsonValueKind.Object || !options.TryGetProperty("model", out var model) ||
            model.ValueKind != JsonValueKind.String || model.GetString() != ModelId)
            throw new ProviderException("modelUnavailable", "Select the installed FLUX.2 klein 4B model in Draw Things. No replacement was selected.");
    }

    public static string ComposePrompt(RepresentationPreset preset, string userPrompt)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(userPrompt);
        // Keep authorship intact. No camera JSON, material swatches, names, paths or token truncation.
        string prompt = string.Join("\n\n", new[] { preset.BaseInstruction, PreservationRules.Objective,
            preset.VegetationPolicy, "Keep any surrounding blank margin unchanged.", userPrompt }.Where(s => !string.IsNullOrWhiteSpace(s)));
        ValidatePrompt(prompt);
        return prompt;
    }

    public async Task<byte[]> EditAsync(byte[] preparedReference, string effectivePrompt, uint seed = 1, CancellationToken ct = default)
    {
        ValidatePrompt(effectivePrompt);
        if (preparedReference.LongLength > MaximumImageBytes)
            throw new ArgumentException("The local test reference exceeds its byte limit.");
        byte[] reference = preparedReference.ToArray();
        // Experimental ceiling for this 16 GB Mac; not the model's theoretical maximum.
        var size = PngHeader.Read(reference);
        if (size.Width < 128 || size.Height < 128 ||
            size.Width > 1536 || size.Height > 1536 || size.Pixels > 1_572_864 || size.Width % 64 != 0 || size.Height % 64 != 0)
            throw new ArgumentException("The local test needs a prepared PNG with edges divisible by 64, at most 1536 pixels and 1.5 megapixels. No automatic crop or resize is applied.");
        await CheckModelAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            model = ModelId,
            init_images = new[] { Convert.ToBase64String(reference) },
            prompt = effectivePrompt,
            negative_prompt = "",
            width = size.Width,
            height = size.Height,
            seed,
            steps = 4,
            guidance_scale = 1.0,
            strength = 1.0, // Klein uses image editing conditioning; not a universal fidelity slider.
            batch_count = 1,
            batch_size = 1,
            loras = Array.Empty<object>(),
            controls = Array.Empty<object>(),
            hires_fix = false,
            upscaler = (string?)null,
            refiner_model = (string?)null,
            restore_faces = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "sdapi/v1/img2img") { Content = new ByteArrayContent(payload) };
        request.Content.Headers.ContentType = new("application/json");
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var body = await ReadJsonAsync(response, 23 * 1024 * 1024, ct).ConfigureAwait(false);
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("images", out var outputs) ||
                outputs.ValueKind != JsonValueKind.Array || outputs.GetArrayLength() != 1 || outputs[0].ValueKind != JsonValueKind.String)
                throw new ProviderException("invalidResult", "Draw Things did not return one encoded image. No URL was followed and no retry was made.");
            byte[] png = Convert.FromBase64String(outputs[0].GetString()!);
            if (png.LongLength > MaximumImageBytes || PngHeader.Read(png) != size)
                throw new ProviderException("invalidResult", "The local output does not match the prepared frame or exceeds the byte limit.");
            return png;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new ProviderException("generationUnconfirmed", "Stopped waiting for Draw Things. It may still finish; no cancellation or automatic retry was performed.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            throw new ProviderException("invalidResult", "Draw Things returned an invalid image response. No automatic retry was made.");
        }
        finally { Array.Clear(payload); }
    }

    private static void ValidatePrompt(string prompt)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(prompt) > MaximumPromptBytes ||
            !prompt.Contains(PreservationRules.Objective, StringComparison.Ordinal))
            throw new ArgumentException("The local test prompt must retain the preservation objective and fit its 2048-byte experiment budget. Shorten it explicitly; it will not be silently truncated.");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, int maximum, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            throw new ProviderException("localEngineRejected", $"Draw Things rejected the request (HTTP {(int)response.StatusCode}). No automatic retry was made.");
        if (response.Content.Headers.ContentType?.MediaType != "application/json" || response.Content.Headers.ContentLength > maximum)
            throw new ProviderException("invalidResponse", "Draw Things returned an unsupported or oversized response.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > maximum)
                throw new ProviderException("invalidResponse", "Draw Things returned an oversized response.");
            bytes.Write(buffer, 0, count);
        }
        try { return SubmissionValidator.Deserialize<JsonElement>(bytes.ToArray()); }
        catch (JsonException) { throw new ProviderException("invalidResponse", "Draw Things returned invalid JSON."); }
    }

    public void Dispose() => client.Dispose();
}

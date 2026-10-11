using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

public sealed class LocalProviderClient : IRenderProvider
{
    private readonly HttpClient client;
    private CapabilitiesResponse? capabilities;
    public LocalProviderClient(Uri endpoint, string token, HttpMessageHandler? handler = null)
    {
        EndpointPolicy.RequireLocalEndpoint(endpoint);
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsControl)) throw new ArgumentException("A session token is required.");
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<CapabilitiesResponse> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var value = await JsonAsync<CapabilitiesResponse>(HttpMethod.Get, "/api/v1/capabilities", null, cancellationToken).ConfigureAwait(false);
        if (value.ProtocolVersion != "1.0" || value.Models is null || !value.SceneSchemaVersions.Contains("1.0") ||
            value.MaximumImageBytes <= 0 || value.MaximumImageBytes > 67_108_864 || value.MaximumDecodedPixels <= 0 ||
            !value.Models.Any(m => m.Id == ReferenceCapabilities.ModelId && m.IsDevelopmentPassthrough && !m.SupportsPrompt))
            throw new ProviderException("unsupportedCapabilities", "The server does not support this capture-test protocol.");
        capabilities = value;
        return value;
    }

    public async Task<ProviderResult> RenderAsync(SceneManifest scene, IReadOnlyDictionary<string, byte[]> assets,
        RenderIntent intent, ResolutionPlan plan, IProgress<ProviderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var caps = capabilities ?? await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (assets.Count != 1 || !assets.TryGetValue("reference.png", out var image)) throw new ArgumentException("Only reference.png is supported.");
        // Serialize and clone once: retries use exactly the same immutable bytes and idempotency key.
        image = image.ToArray();
        byte[] sceneBytes = JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options);
        string sceneHash = ContentDigest.Sha256(sceneBytes);
        var submission = new JobSubmission { ClientRequestId = scene.SceneId, ModelId = ReferenceCapabilities.ModelId,
            ModelVersion = ReferenceCapabilities.ModelVersion, CapabilityRevision = caps.CapabilityRevision,
            Intent = intent, Plan = plan, SceneDigest = sceneHash };
        var issues = SubmissionValidator.Validate(submission, scene, image, caps);
        if (issues.Count != 0) throw new ProviderException(issues[0].Code, issues[0].Message);
        byte[] requestBytes = JsonSerializer.SerializeToUtf8Bytes(submission, WireJson.Options);
        if (sceneBytes.Length > caps.MaximumJsonBytes || requestBytes.Length > caps.MaximumJsonBytes) throw new ProviderException("metadataTooLarge", "The scene metadata exceeds the server limit.");
        JobResponse? job = null;
        try
        {
            progress?.Report(new("uploading"));
            string key = scene.SceneId.ToString("D"); // One development job per snapshot; changed request bytes conflict safely.
            for (int attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var multipart = new MultipartFormDataContent();
                Add(multipart, requestBytes, "request", "application/json");
                Add(multipart, sceneBytes, "scene", "application/json");
                Add(multipart, image, "reference.png", "image/png");
                using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/jobs") { Content = multipart };
                message.Headers.Add("Idempotency-Key", key);
                try { job = await SendJsonAsync<JobResponse>(message, cancellationToken).ConfigureAwait(false); break; }
                catch (HttpRequestException) when (attempt == 0) { await Task.Delay(300, cancellationToken).ConfigureAwait(false); }
            }
            Guid acceptedJobId = job.Id;
            long lastRevision = -1;
            while (!job.IsTerminal)
            {
                if (job.Id == Guid.Empty || job.Id != acceptedJobId || job.Revision < lastRevision) throw new ProviderException("invalidJob", "The server returned inconsistent job state.");
                lastRevision = job.Revision;
                progress?.Report(new(job.State == ServerJobState.Queued ? "queued" : "rendering", job.Id));
                await Task.Delay(1000 + Random.Shared.Next(250), cancellationToken).ConfigureAwait(false);
                job = await JsonAsync<JobResponse>(HttpMethod.Get, $"/api/v1/jobs/{job.Id:D}", null, cancellationToken).ConfigureAwait(false);
            }
            return await DownloadAsync(job, scene, image, sceneHash, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (job is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    var cancelled = await JsonAsync<JobResponse>(HttpMethod.Post, $"/api/v1/jobs/{job.Id:D}/cancel", null, timeout.Token).ConfigureAwait(false);
                    if (cancelled.State == ServerJobState.Completed) return await DownloadAsync(cancelled, scene, image, sceneHash, progress, timeout.Token).ConfigureAwait(false);
                    if (cancelled.State == ServerJobState.Cancelled) throw new ProviderException("cancelled", "Capture test cancelled.", job.Id);
                }
                catch (ProviderException ex) when (ex.Code == "cancelled") { throw; }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or ProviderException) { }
            }
            throw new ProviderException("cancelUnconfirmed", "Stopped waiting. The local server may still finish the accepted capture test.", job?.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { throw new ProviderException("connectionLost", "The connection was lost. An accepted job may still finish on the server.", job?.Id); }
    }

    private async Task<ProviderResult> DownloadAsync(JobResponse job, SceneManifest scene, byte[] image, string sceneHash, IProgress<ProviderProgress>? progress, CancellationToken ct)
    {
        if (job.State != ServerJobState.Completed || job.Result is not { } result)
            throw new ProviderException(job.Failure?.Code ?? "cancelled", "The capture test did not complete.", job.Id);
        if (result.Outputs is not { Count: 1 } || result.SceneId != scene.SceneId || result.SceneDigest != sceneHash || !result.IsUnchangedReference || !result.Plan.IsExact)
            throw new ProviderException("invalidResult", "The result does not match the captured scene.", job.Id);
        var output = result.Outputs[0];
        string path = EndpointPolicy.RequireOutputPath(output.DownloadPath, job.Id, output.OutputId);
        if (output.MimeType != "image/png" || output.Size != scene.Frame || output.ByteCount != image.LongLength || output.Sha256 != ContentDigest.Sha256(image))
            throw new ProviderException("invalidResult", "The output metadata differs from the unchanged reference.", job.Id);
        progress?.Report(new("downloading", job.Id));
        using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await RequireSuccess(response, ct).ConfigureAwait(false);
        if (response.Content.Headers.ContentType?.MediaType != "image/png") throw new ProviderException("invalidResult", "The output is not a PNG image.", job.Id);
        byte[] bytes = await ReadBounded(response.Content, image.LongLength, ct).ConfigureAwait(false);
        if (!bytes.AsSpan().SequenceEqual(image) || PngHeader.Read(bytes) != scene.Frame) throw new ProviderException("invalidResult", "The returned image failed reference validation.", job.Id);
        return new(bytes, job, sceneHash);
    }

    private async Task<T> JsonAsync<T>(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    { using var request = new HttpRequestMessage(method, path) { Content = content }; return await SendJsonAsync<T>(request, ct).ConfigureAwait(false); }
    private async Task<T> SendJsonAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await RequireSuccess(response, ct).ConfigureAwait(false);
        if (response.Content.Headers.ContentType?.MediaType != "application/json") throw new ProviderException("invalidResponse", "Expected a JSON server response.");
        try { return SubmissionValidator.Deserialize<T>(await ReadBounded(response.Content, 2_097_152, ct).ConfigureAwait(false)); }
        catch (JsonException) { throw new ProviderException("invalidResponse", "The server returned invalid JSON."); }
    }
    private static async Task RequireSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string code = response.StatusCode == HttpStatusCode.Unauthorized ? "authenticationRequired" : "serverRejected";
        try
        {
            var problem = JsonSerializer.Deserialize<ProblemResponse>(await ReadBounded(response.Content, 65_536, ct).ConfigureAwait(false), WireJson.Options);
            if (problem?.Code is { Length: > 0 and < 80 } c && c.All(char.IsAsciiLetterOrDigit)) code = c;
        }
        catch (JsonException) { }
        throw new ProviderException(code, $"The reference server rejected the request (HTTP {(int)response.StatusCode}, {code}).");
    }
    private static async Task<byte[]> ReadBounded(HttpContent content, long maximum, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maximum) throw new ProviderException("responseTooLarge", "The server response exceeds its byte limit.");
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var result = new MemoryStream(); var buffer = new byte[81920]; int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        { if (result.Length + count > maximum) throw new ProviderException("responseTooLarge", "The server response exceeds its byte limit."); result.Write(buffer, 0, count); }
        return result.ToArray();
    }
    private static void Add(MultipartFormDataContent multipart, byte[] bytes, string name, string mime)
    { var part = new ByteArrayContent(bytes); part.Headers.ContentType = new(mime); multipart.Add(part, name, name == "reference.png" ? name : name + ".json"); }
    public void Dispose() => client.Dispose();
}

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
    private readonly string modelId;
    private readonly IImageProcessor? images;
    public LocalProviderClient(Uri endpoint, string token, HttpMessageHandler? handler = null, string modelId = ReferenceCapabilities.ModelId, IImageProcessor? images = null)
    {
        EndpointPolicy.RequireLocalEndpoint(endpoint);
        if (modelId != ReferenceCapabilities.ModelId && modelId != ManagedModel.Id)
            throw new ArgumentException("The local model is not supported.");
        this.modelId = modelId;
        this.images = images;
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsControl))
            throw new ArgumentException("A session token is required.");
        client = new HttpClient(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false
        })
        {
            BaseAddress = endpoint,
            Timeout = TimeSpan.FromSeconds(45)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    async Task<ProviderCapabilities> IRenderProvider.DiscoverAsync(CancellationToken cancellationToken)
    {
        var capabilities = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        return new()
        {
            ProviderId = "local",
            CapabilityRevision = capabilities.CapabilityRevision,
            Models = capabilities.Models.Where(m => m.Id == modelId).ToArray(),
            MaximumImageBytes = capabilities.MaximumImageBytes,
            MaximumDecodedPixels = capabilities.MaximumDecodedPixels
        };
    }

    public async Task<CapabilitiesResponse> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var value = await JsonAsync<CapabilitiesResponse>(HttpMethod.Get, "/api/v1/capabilities", null, cancellationToken).ConfigureAwait(false);
        if (value.ProtocolVersion != "1.0" || value.Models is null || !value.SceneSchemaVersions.Contains("1.0") || value.MaximumImageBytes <= 0 || value.MaximumImageBytes > 67_108_864 || value.MaximumDecodedPixels <= 0 || !value.Models.Any(m => m.Id == ReferenceCapabilities.ModelId && m.IsDevelopmentPassthrough && !m.SupportsPrompt))
            throw new ProviderException("unsupportedCapabilities", "The server does not support this capture-test protocol.");
        if (value.Models.Count(m => m.Id == modelId) != 1 || modelId == ManagedModel.Id &&
            !SameManagedProfile(value.Models.Single(m => m.Id == modelId)))
            throw new ProviderException("modelUnavailable", "The selected local model is not ready. Check it in VisoLoom Settings.");
        capabilities = value;
        return value;
    }

    public Task<ProviderResult> RenderAsync(SceneManifest scene, IReadOnlyDictionary<string, byte[]> assets, RenderIntent intent, ResolutionPlan plan, IProgress<ProviderProgress>? progress = null, CancellationToken cancellationToken = default)
        => RenderCoreAsync(scene, assets, intent, plan, null, progress, cancellationToken);

    public Task<ProviderResult> RenderPreparedAsync(SceneManifest scene, IReadOnlyDictionary<string, byte[]> assets,
        RenderIntent intent, ResolutionPlan plan, byte[] preparedReference, IProgress<ProviderProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => RenderCoreAsync(scene, assets, intent, plan, preparedReference, progress, cancellationToken);

    private async Task<ProviderResult> RenderCoreAsync(SceneManifest scene, IReadOnlyDictionary<string, byte[]> assets,
        RenderIntent intent, ResolutionPlan plan, byte[]? preparedReference, IProgress<ProviderProgress>? progress, CancellationToken cancellationToken)
    {
        var caps = capabilities ?? await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (assets.Count != 1 || !assets.TryGetValue("reference.png", out var image))
            throw new ArgumentException("Only reference.png is supported.");
        if (modelId == ManagedModel.Id)
        {
            if (SceneValidator.Validate(scene).Count != 0)
                throw new ProviderException("invalidAsset", "A valid captured scene is required.");
            var pass = scene.Passes.Single(p => p.Kind == PassKind.Reference && p.Status == PassStatus.Available);
            if (SceneValidator.Validate(scene).Count != 0 || PngHeader.Read(image) != scene.Frame ||
                pass.ByteCount != image.LongLength || pass.Sha256 != ContentDigest.Sha256(image) || images is null)
                throw new ProviderException("invalidAsset", "The reference does not match the captured scene.");
            image = preparedReference ?? images.PrepareReference(image, plan);
        }
        // Serialize and clone once: retries use exactly the same immutable bytes and idempotency key.
        image = image.ToArray();
        byte[] sceneBytes = JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options);
        string sceneHash = ContentDigest.Sha256(sceneBytes);
        var submission = new JobSubmission
        {
            ClientRequestId = scene.SceneId,
            ModelId = modelId,
            ModelVersion = caps.Models.Single(m => m.Id == modelId).Version,
            CapabilityRevision = caps.CapabilityRevision,
            Intent = intent,
            Plan = plan,
            SceneDigest = sceneHash,
            PreparedReference = modelId == ManagedModel.Id ? new(plan.Native, image.LongLength, ContentDigest.Sha256(image)) : null
        };
        var issues = SubmissionValidator.Validate(submission, scene, image, caps);
        if (issues.Count != 0)
            throw new ProviderException(issues[0].Code, issues[0].Message);
        byte[] requestBytes = JsonSerializer.SerializeToUtf8Bytes(submission, WireJson.Options);
        if (sceneBytes.Length > caps.MaximumJsonBytes || requestBytes.Length > caps.MaximumJsonBytes)
            throw new ProviderException("metadataTooLarge", "The scene metadata exceeds the server limit.");
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
                using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/jobs")
                {
                    Content = multipart
                };
                message.Headers.Add("Idempotency-Key", key);
                try
                {
                    job = await SendJsonAsync<JobResponse>(message, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (HttpRequestException) when (attempt == 0)
                {
                    await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                }
            }

            Guid acceptedJobId = job.Id;
            long lastRevision = -1;
            while (!job.IsTerminal)
            {
                if (job.Id == Guid.Empty || job.Id != acceptedJobId || job.Revision < lastRevision)
                    throw new ProviderException("invalidJob", "The server returned inconsistent job state.");
                lastRevision = job.Revision;
                progress?.Report(new(job.State == ServerJobState.Queued ? "queued" : "rendering", job.Id));
                await Task.Delay(1000 + Random.Shared.Next(250), cancellationToken).ConfigureAwait(false);
                job = await JsonAsync<JobResponse>(HttpMethod.Get, $"/api/v1/jobs/{job.Id:D}", null, cancellationToken).ConfigureAwait(false);
            }

            return await DownloadAsync(job, scene, image, sceneHash, plan, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (job is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    var cancelled = await JsonAsync<JobResponse>(HttpMethod.Post, $"/api/v1/jobs/{job.Id:D}/cancel", null, timeout.Token).ConfigureAwait(false);
                    while (!cancelled.IsTerminal)
                    {
                        await Task.Delay(100, timeout.Token).ConfigureAwait(false);
                        cancelled = await JsonAsync<JobResponse>(HttpMethod.Get, $"/api/v1/jobs/{job.Id:D}", null, timeout.Token).ConfigureAwait(false);
                    }
                    if (cancelled.State == ServerJobState.Completed)
                        return await DownloadAsync(cancelled, scene, image, sceneHash, plan, progress, timeout.Token).ConfigureAwait(false);
                    if (cancelled.State == ServerJobState.Cancelled)
                        throw new ProviderException("cancelled", "Local job cancelled after its worker stopped.", job.Id);
                    if (cancelled.State == ServerJobState.Failed)
                        throw new ProviderException("cancelled", "The local job stopped without a result.", job.Id);
                }
                catch (ProviderException ex) when (ex.Code == "cancelled")
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or ProviderException)
                {
                }
            }

            throw new ProviderException("cancelUnconfirmed", "Stopped waiting. The local server may still finish the accepted job.", job?.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ProviderException("connectionLost", "The connection was lost. An accepted job may still finish on the server.", job?.Id);
        }
    }

    private async Task<ProviderResult> DownloadAsync(JobResponse job, SceneManifest scene, byte[] image, string sceneHash, ResolutionPlan plan, IProgress<ProviderProgress>? progress, CancellationToken ct)
    {
        if (job.State != ServerJobState.Completed || job.Result is not { } result)
            throw new ProviderException(job.Failure?.Code ?? "cancelled", $"The local job did not complete ({job.Failure?.Code ?? "cancelled"}).", job.Id);
        if (result.Outputs is not { Count: 1 } || result.SceneId != scene.SceneId || result.SceneDigest != sceneHash || (modelId == ReferenceCapabilities.ModelId ? !result.IsUnchangedReference || !result.Plan.IsExact : result.IsUnchangedReference) || result.ModelId != modelId || result.Plan != plan || result.ModelVersion != capabilities!.Models.Single(m => m.Id == modelId).Version)
            throw new ProviderException("invalidResult", "The result does not match the captured scene.", job.Id);
        var output = result.Outputs[0];
        string path = EndpointPolicy.RequireOutputPath(output.DownloadPath, job.Id, output.OutputId);
        if (output.MimeType != "image/png" || output.Size != plan.Native || output.ByteCount <= 0 || output.ByteCount > (modelId == ManagedModel.Id ? 16_777_216 : capabilities!.MaximumImageBytes) ||
            modelId == ReferenceCapabilities.ModelId && (output.ByteCount != image.LongLength || output.Sha256 != ContentDigest.Sha256(image)))
            throw new ProviderException("invalidResult", "The output metadata differs from the unchanged reference.", job.Id);
        progress?.Report(new("downloading", job.Id));
        using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await RequireSuccess(response, ct).ConfigureAwait(false);
        if (response.Content.Headers.ContentType?.MediaType != "image/png")
            throw new ProviderException("invalidResult", "The output is not a PNG image.", job.Id);
        byte[] bytes = await ReadBounded(response.Content, output.ByteCount, ct).ConfigureAwait(false);
        if (bytes.LongLength != output.ByteCount || ContentDigest.Sha256(bytes) != output.Sha256 || PngHeader.Read(bytes) != plan.Native ||
            modelId == ReferenceCapabilities.ModelId && !bytes.AsSpan().SequenceEqual(image))
            throw new ProviderException("invalidResult", "The returned image failed reference validation.", job.Id);
        byte[] native = bytes;
        if (modelId == ManagedModel.Id)
        {
            progress?.Report(new("processing", job.Id));
            bytes = images!.NormalizeResult(native, plan);
            if (PngHeader.Read(bytes) != plan.Final)
                throw new ProviderException("invalidResult", "The restored local image has the wrong dimensions.");
        }
        return new(bytes, new RenderResult
        {
            ResultId = job.Id,
            SceneId = scene.SceneId,
            SceneDigest = sceneHash,
            ProviderId = "local",
            ModelId = result.ModelId,
            ModelVersion = result.ModelVersion,
            Plan = result.Plan,
            CompletedAt = job.UpdatedAt,
            RemoteJobId = job.Id.ToString("D"),
            OutputSha256 = ContentDigest.Sha256(bytes),
            OutputByteCount = bytes.LongLength,
            NativeOutputSha256 = modelId == ManagedModel.Id ? output.Sha256 : null,
            NativeOutputByteCount = modelId == ManagedModel.Id ? output.ByteCount : null,
            IsUnchangedReference = result.IsUnchangedReference,
            ConsumedPasses = result.ConsumedPasses,
            OmittedPasses = result.OmittedPasses,
            ProcessingHistory = result.ProcessingHistory,
            Warnings = result.Warnings
        }) { NativePngBytes = modelId == ManagedModel.Id ? native : null };
    }

    private static bool SameManagedProfile(ModelCapabilities model)
    {
        var expected = ManagedModel.Create(model.Version);
        var limits = model.Resolution;
        return model.Version.Length is > 0 and < 128 && model.SupportsPrompt && model.SupportsCancellation &&
            model.SupportsPadding && model.SupportsResampling && !model.IsDevelopmentPassthrough &&
            model.RequiredPasses is not null && model.AcceptedPasses is not null &&
            model.RequiredPasses.SequenceEqual(expected.RequiredPasses) && model.AcceptedPasses.SequenceEqual(expected.AcceptedPasses) &&
            limits is not null && limits.IsKnown && limits.MinEdge == expected.Resolution.MinEdge && limits.MaxEdge == expected.Resolution.MaxEdge &&
            limits.MinPixels == expected.Resolution.MinPixels && limits.MaxPixels == expected.Resolution.MaxPixels &&
            limits.DimensionMultiple == 64 && limits.AllowFractionalSampling && !limits.AllowUpscaling &&
            limits.MaximumAspectRatio is null && limits.FixedSizes is { Count: 0 } && limits.AllowedRatios is { Count: 0 };
    }

    private async Task<T> JsonAsync<T>(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = content
        };
        return await SendJsonAsync<T>(request, ct).ConfigureAwait(false);
    }

    private async Task<T> SendJsonAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await RequireSuccess(response, ct).ConfigureAwait(false);
        if (response.Content.Headers.ContentType?.MediaType != "application/json")
            throw new ProviderException("invalidResponse", "Expected a JSON server response.");
        try
        {
            return SubmissionValidator.Deserialize<T>(await ReadBounded(response.Content, 2_097_152, ct).ConfigureAwait(false));
        }
        catch (JsonException)
        {
            throw new ProviderException("invalidResponse", "The server returned invalid JSON.");
        }
    }

    private static async Task RequireSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        string code = response.StatusCode == HttpStatusCode.Unauthorized ? "authenticationRequired" : "serverRejected";
        try
        {
            var problem = JsonSerializer.Deserialize<ProblemResponse>(await ReadBounded(response.Content, 65_536, ct).ConfigureAwait(false), WireJson.Options);
            if (problem?.Code is { Length: > 0 and < 80 } c && c.All(char.IsAsciiLetterOrDigit))
                code = c;
        }
        catch (JsonException)
        {
        }

        throw new ProviderException(code, $"The reference server rejected the request (HTTP {(int)response.StatusCode}, {code}).");
    }

    private static async Task<byte[]> ReadBounded(HttpContent content, long maximum, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maximum)
            throw new ProviderException("responseTooLarge", "The server response exceeds its byte limit.");
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var result = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (result.Length + count > maximum)
                throw new ProviderException("responseTooLarge", "The server response exceeds its byte limit.");
            result.Write(buffer, 0, count);
        }

        return result.ToArray();
    }

    private static void Add(MultipartFormDataContent multipart, byte[] bytes, string name, string mime)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new(mime);
        multipart.Add(part, name, name == "reference.png" ? name : name + ".json");
    }

    public void Dispose() => client.Dispose();
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using VisoLoom.Core;
using VisoLoom.Protocol;
using VisoLoom.Providers;
using VisoLoom.ReferenceServer;
using VisoLoom.Tests;
using Xunit;

namespace VisoLoom.Protocol.Tests;

public sealed class HttpIntegrationTests
{
    [Fact]
    public async Task AutomaticConnectionAuthenticatesAndReturnsUnchangedCapture()
    {
        string pipeName = "vl-" + Guid.NewGuid().ToString("N")[..16];
        await using var host = await RunningServer.Start(configure: o => o with { DevelopmentPipeName = pipeName });
        var connection = await LocalDevelopmentConnection.FindAsync(pipeName: pipeName);
        Assert.NotNull(connection);
        Assert.Equal(host.Origin, connection.Endpoint);
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var repeated = await LocalDevelopmentConnection.FindAsync(pipeName: pipeName);
            Assert.NotNull(repeated);
            Assert.Equal(connection.Token, repeated.Token);
        }
        using var provider = new LocalProviderClient(connection.Endpoint, connection.Token);
        Assert.True((await provider.DiscoverAsync()).Models.Single().IsDevelopmentPassthrough);
        var scene = SceneFixture.Create();
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = SceneFixture.Png },
            new() { RequestedOutput = scene.Frame }, SceneFixture.Plan(scene.Frame));
        Assert.Equal(SceneFixture.Png, result.PngBytes);
        using var anonymous = new HttpClient { BaseAddress = connection.Endpoint };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/capabilities")).StatusCode);
    }

    [Fact]
    public async Task AutomaticConnectionRefreshesRotatedTokenAfterServiceRestart()
    {
        string pipeName = "vl-" + Guid.NewGuid().ToString("N")[..16];
        await using var host = await RunningServer.Start(configure: o => o with { DevelopmentPipeName = pipeName });
        var before = await LocalDevelopmentConnection.FindAsync(pipeName: pipeName);
        Assert.NotNull(before);
        await host.Restart(rotateToken: true);
        var after = await LocalDevelopmentConnection.FindAsync(pipeName: pipeName);
        Assert.NotNull(after);
        Assert.Equal(before.Endpoint, after.Endpoint);
        Assert.NotEqual(before.Token, after.Token);
        using var stale = new LocalProviderClient(before.Endpoint, before.Token);
        await Assert.ThrowsAsync<ProviderException>(() => stale.DiscoverAsync());
        using var fresh = new LocalProviderClient(after.Endpoint, after.Token);
        await fresh.DiscoverAsync();
    }

    [Fact]
    public async Task CompleteProviderRoundTripReturnsIdenticalPng()
    {
        await using var host = await RunningServer.Start();
        using var provider = new LocalProviderClient(host.Origin, host.Token);
        var capabilities = await ((IRenderProvider)provider).DiscoverAsync();
        Assert.Equal("local", capabilities.ProviderId);
        Assert.True(capabilities.Models.Single().IsDevelopmentPassthrough);
        var scene = SceneFixture.Create();
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = SceneFixture.Png }, new() { RequestedOutput = scene.Frame }, SceneFixture.Plan(scene.Frame));
        Assert.Equal(SceneFixture.Png, result.PngBytes);
        Assert.True(result.Metadata.IsUnchangedReference);
        Assert.Equal("local", result.Metadata.ProviderId);
    }

    [Fact]
    public async Task ReconnectingSameCaptureReusesAcceptedJob()
    {
        await using var host = await RunningServer.Start();
        var scene = SceneFixture.Create();
        var assets = new Dictionary<string, byte[]>
        {
            ["reference.png"] = SceneFixture.Png
        };
        var intent = new RenderIntent
        {
            RequestedOutput = scene.Frame
        };
        using var firstClient = new LocalProviderClient(host.Origin, host.Token);
        var first = await firstClient.RenderAsync(scene, assets, intent, SceneFixture.Plan(scene.Frame));
        using var reconnected = new LocalProviderClient(host.Origin, host.Token);
        var second = await reconnected.RenderAsync(scene, assets, intent, SceneFixture.Plan(scene.Frame));
        Assert.Equal(first.Metadata.ResultId, second.Metadata.ResultId);
        Assert.Equal(first.PngBytes, second.PngBytes);
    }

    [Fact]
    public async Task NullCollectionsAreReportedAsValidationErrors()
    {
        await using var host = await RunningServer.Start();
        using var client = host.Client();
        var upload = Upload.Create();
        using var document = JsonDocument.Parse(upload.Scene);
        var scene = JsonSerializer.Deserialize<SceneManifest>(upload.Scene, WireJson.Options)! with
        {
            Passes = null!
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(scene, new JsonSerializerOptions(WireJson.Options) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never });
        upload = new Upload
        {
            Scene = bytes,
            Request = upload.Request with
            {
                SceneDigest = ContentDigest.Sha256(bytes)
            }
        };
        var response = await upload.Send(client, "null-passes");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticationAndBrowserOriginsAreRejected()
    {
        await using var host = await RunningServer.Start();
        using var anonymous = new HttpClient
        {
            BaseAddress = host.Origin
        };
        var response = await anonymous.GetAsync("/api/v1/capabilities");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = JsonSerializer.Deserialize<ProblemResponse>(await response.Content.ReadAsByteArrayAsync(), WireJson.Options)!;
        Assert.Equal(401, error.Status);
        Assert.Equal("authenticationRequired", error.Code);
        using var browser = host.Client();
        browser.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:1234");
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("/api/v1/capabilities")).StatusCode);
        using var wrongHost = host.Client();
        wrongHost.DefaultRequestHeaders.Host = "evil.example";
        Assert.Equal(HttpStatusCode.Forbidden, (await wrongHost.GetAsync("/api/v1/capabilities")).StatusCode);
    }

    [Fact]
    public async Task LostSubmissionCanBeReconciledAndConflictingReplayRejected()
    {
        await using var host = await RunningServer.Start(slow: true);
        using var client = host.Client();
        var upload = Upload.Create();
        var first = await Read<JobResponse>(await upload.Send(client, "same-key"));
        var retry = await Read<JobResponse>(await upload.Send(client, "same-key"));
        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Upload.Create().Send(client, "same-key")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/jobs/{Guid.NewGuid():D}")).StatusCode);
    }

    [Fact]
    public async Task QueueCancellationIsTerminalAndIdempotent()
    {
        await using var host = await RunningServer.Start(slow: true);
        using var client = host.Client();
        var job = await Read<JobResponse>(await Upload.Create().Send(client, "cancel-me"));
        var cancelled = await Read<JobResponse>(await client.PostAsync($"/api/v1/jobs/{job.Id:D}/cancel", null));
        var again = await Read<JobResponse>(await client.PostAsync($"/api/v1/jobs/{job.Id:D}/cancel", null));
        Assert.Equal(ServerJobState.Cancelled, cancelled.State);
        Assert.True(cancelled.CancelRequested);
        Assert.Equal(cancelled.Revision, again.Revision);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/jobs/{job.Id:D}/outputs/reference")).StatusCode);
    }

    [Fact]
    public async Task RestartMarksUnfinishedJobsFailedAndPreservesIdempotency()
    {
        await using var host = await RunningServer.Start(slow: true);
        var upload = Upload.Create();
        Guid id;
        using (var client = host.Client())
            id = (await Read<JobResponse>(await upload.Send(client, "restart"))).Id;
        await host.Restart();
        using var after = host.Client();
        var job = await Read<JobResponse>(await after.GetAsync($"/api/v1/jobs/{id:D}"));
        Assert.Equal(ServerJobState.Failed, job.State);
        Assert.Equal("serverRestarted", job.Failure?.Code);
        Assert.Equal(id, (await Read<JobResponse>(await upload.Send(after, "restart"))).Id);
    }

    [Fact]
    public async Task CompletedOutputSurvivesRestartAndCancellationDoesNotEraseIt()
    {
        await using var host = await RunningServer.Start();
        using var provider = new LocalProviderClient(host.Origin, host.Token);
        var scene = SceneFixture.Create();
        var result = await provider.RenderAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = SceneFixture.Png }, new() { RequestedOutput = scene.Frame }, SceneFixture.Plan(scene.Frame));
        await host.Restart();
        using var client = host.Client();
        var job = await Read<JobResponse>(await client.PostAsync($"/api/v1/jobs/{result.Metadata.ResultId:D}/cancel", null));
        Assert.Equal(ServerJobState.Completed, job.State);
        Assert.Equal(SceneFixture.Png, await client.GetByteArrayAsync(job.Result!.Outputs[0].DownloadPath));
    }

    [Fact]
    public async Task UploadValidationRejectsTamperedAndUndeclaredAssets()
    {
        await using var host = await RunningServer.Start();
        using var client = host.Client();
        var tampered = Upload.Create();
        tampered.Reference[33] ^= 1;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await tampered.Send(client, "tampered")).StatusCode);
        var upload = Upload.Create();
        Assert.Equal(HttpStatusCode.BadRequest, (await upload.Send(client, "traversal", fileName: "../reference.png")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await upload.Send(client, "extra", extra: true)).StatusCode);
        var prompt = Upload.Create();
        prompt.Request = prompt.Request with
        {
            Intent = prompt.Request.Intent with
            {
                UserPrompt = "make a sketch"
            }
        };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await prompt.Send(client, "prompt")).StatusCode);
    }

    [Fact]
    public async Task FullQueueAndByteLimitsReturnStructuredErrors()
    {
        await using var host = await RunningServer.Start(slow: true, configure: o => o with
        {
            QueueCapacity = 1,
            MaximumImageBytes = 1024,
            MaximumRequestBytes = 8192
        });
        using var client = host.Client();
        Assert.Equal(HttpStatusCode.Accepted, (await Upload.Create().Send(client, "one")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Upload.Create().Send(client, "two")).StatusCode);
        using var huge = new ByteArrayContent(new byte[9000]);
        huge.Headers.ContentType = new("multipart/form-data");
        var response = await client.PostAsync("/api/v1/jobs", huge); // Key validation precedes body parsing.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/jobs")
        {
            Content = new ByteArrayContent(new byte[9000])
        };
        message.Headers.Add("Idempotency-Key", "large");
        message.Content.Headers.ContentType = new("multipart/form-data");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.SendAsync(message)).StatusCode);
    }

    [Fact]
    public async Task KnownExpiredJobsReturnGone()
    {
        await using var host = await RunningServer.Start(slow: true, configure: o => o with { Retention = TimeSpan.FromMilliseconds(20) });
        using var client = host.Client();
        var job = await Read<JobResponse>(await Upload.Create().Send(client, "expire"));
        await client.PostAsync($"/api/v1/jobs/{job.Id:D}/cancel", null);
        await Task.Delay(60);
        Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync($"/api/v1/jobs/{job.Id:D}")).StatusCode);
    }

    [Fact]
    public async Task ProviderDoesNotFollowRedirects()
    {
        var calls = 0;
        using var handler = new Handler(request =>
        {
            calls++;
            return new(HttpStatusCode.Redirect)
            {
                Headers =
                {
                    Location = new Uri("https://example.invalid/steal")
                },
                Content = new StringContent("")
            };
        });
        using var provider = new LocalProviderClient(new("http://127.0.0.1:5179"), "test-only-token", handler);
        await Assert.ThrowsAsync<ProviderException>(() => provider.DiscoverAsync());
        Assert.Equal(1, calls);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(handle(request));
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<T>(await response.Content.ReadAsByteArrayAsync(), WireJson.Options)!;
    }

    private sealed class Upload
    {
        private string? discoveredRevision;
        public required JobSubmission Request { get; set; }
        public required byte[] Scene { get; init; }
        public byte[] Reference { get; init; } = SceneFixture.Png;

        public static Upload Create()
        {
            var scene = SceneFixture.Create();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options);
            return new()
            {
                Scene = bytes,
                Request = new()
                {
                    ClientRequestId = Guid.NewGuid(),
                    ModelId = ReferenceCapabilities.ModelId,
                    ModelVersion = ReferenceCapabilities.ModelVersion,
                    CapabilityRevision = ReferenceCapabilities.Revision,
                    SceneDigest = ContentDigest.Sha256(bytes),
                    Intent = new()
                    {
                        RequestedOutput = scene.Frame
                    },
                    Plan = SceneFixture.Plan(scene.Frame)
                }
            };
        }

        public async Task<HttpResponseMessage> Send(HttpClient client, string key, string fileName = "reference.png", bool extra = false)
        {
            discoveredRevision ??= (await Read<CapabilitiesResponse>(await client.GetAsync("/api/v1/capabilities"))).CapabilityRevision;
            Request = Request with
            {
                CapabilityRevision = discoveredRevision
            };
            using var body = new MultipartFormDataContent();
            Add("request", JsonSerializer.SerializeToUtf8Bytes(Request, WireJson.Options), "application/json", "request.json");
            Add("scene", Scene, "application/json", "scene.json");
            Add("reference.png", Reference, "image/png", fileName);
            if (extra)
                Add("geometry", [
                    1,
                2,
                3
                ], "application/octet-stream", "geometry");
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/jobs")
            {
                Content = body
            };
            message.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(message);
            void Add(string name, byte[] bytes, string mime, string file)
            {
                var part = new ByteArrayContent(bytes);
                part.Headers.ContentType = new(mime);
                body.Add(part, name, file);
            }
        }
    }

    [Fact]
    public async Task ManagedEditUsesReviewedBytesAndDurableIdentityWithoutRepeatingInference()
    {
        var engine = new FixtureEngine();
        await using var host = await RunningServer.Start(configure: o => o with { LocalEngine = engine });
        using var provider = new LocalProviderClient(host.Origin, host.Token, modelId: ManagedModel.Id, images: new FixtureCodec());
        var scene = SceneFixture.Create();
        var plan = ResolutionPlanner.Plan(scene.Frame, scene.Frame, engine.Model.Resolution);
        byte[] prepared = HeaderFixture(plan.Native);
        var intent = new RenderIntent { PresetId = "fixture", RequestedOutput = scene.Frame, EffectivePrompt = PreservationRules.Objective + "\nArchitect's exact words." };
        var assets = new Dictionary<string, byte[]> { ["reference.png"] = SceneFixture.Png };
        var result = await provider.RenderPreparedAsync(scene, assets, intent, plan, prepared);
        Assert.False(result.Metadata.IsUnchangedReference);
        Assert.Equal(ManagedModel.Id, result.Metadata.ModelId);
        Assert.Equal(ContentDigest.Sha256(prepared), engine.InputHash);
        Assert.Equal(intent.EffectivePrompt, engine.Submission!.Intent.EffectivePrompt);
        Assert.NotNull(result.NativePngBytes);
        Assert.Equal(SceneFixture.Png, result.PngBytes);
        var recovered = await provider.RenderPreparedAsync(scene, assets, intent, plan, prepared);
        Assert.Equal(result.Metadata.ResultId, recovered.Metadata.ResultId);
        Assert.Equal(1, engine.Calls);
        var conflict = await Assert.ThrowsAsync<ProviderException>(() => provider.RenderPreparedAsync(scene, assets,
            intent with { EffectivePrompt = intent.EffectivePrompt + " changed" }, plan, prepared));
        Assert.Equal("idempotencyConflict", conflict.Code);
        Assert.Equal(1, engine.Calls);
        using var anonymous = new HttpClient { BaseAddress = host.Origin };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/capabilities")).StatusCode);
    }

    [Fact]
    public async Task ManagedCancellationRemainsRunningUntilWorkerConfirmsExit()
    {
        var engine = new FixtureEngine { WaitForRelease = true };
        await using var host = await RunningServer.Start(configure: o => o with { LocalEngine = engine });
        using var provider = new LocalProviderClient(host.Origin, host.Token, modelId: ManagedModel.Id, images: new FixtureCodec());
        var scene = SceneFixture.Create();
        var plan = ResolutionPlanner.Plan(scene.Frame, scene.Frame, engine.Model.Resolution);
        var render = provider.RenderPreparedAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = SceneFixture.Png },
            new() { RequestedOutput = scene.Frame, EffectivePrompt = PreservationRules.Objective }, plan, HeaderFixture(plan.Native));
        Guid id = await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var client = host.Client();
        using var cancellation = await client.PostAsync($"/api/v1/jobs/{id:D}/cancel", null);
        var requested = SubmissionValidator.Deserialize<JobResponse>(await cancellation.Content.ReadAsByteArrayAsync());
        Assert.Equal(ServerJobState.Running, requested.State);
        Assert.True(requested.CancelRequested);
        Assert.False(engine.Stopped);
        engine.Release.SetResult();
        var failure = await Assert.ThrowsAsync<ProviderException>(() => render);
        Assert.Equal("cancelled", failure.Code);
        Assert.True(engine.Stopped);
        Assert.Equal(1, engine.Calls);
    }

    [Fact]
    public async Task ManagedInvalidFrameAndPromptOverflowNeverReachWorker()
    {
        var engine = new FixtureEngine();
        await using var host = await RunningServer.Start(configure: o => o with { LocalEngine = engine });
        using var provider = new LocalProviderClient(host.Origin, host.Token, modelId: ManagedModel.Id, images: new FixtureCodec());
        var scene = SceneFixture.Create();
        var plan = ResolutionPlanner.Plan(scene.Frame, scene.Frame, engine.Model.Resolution);
        var assets = new Dictionary<string, byte[]> { ["reference.png"] = SceneFixture.Png };
        var intent = new RenderIntent { RequestedOutput = scene.Frame, EffectivePrompt = PreservationRules.Objective };
        await Assert.ThrowsAsync<ProviderException>(() => provider.RenderPreparedAsync(scene, assets, intent, plan, SceneFixture.Png));
        await Assert.ThrowsAsync<ProviderException>(() => provider.RenderPreparedAsync(scene, assets,
            intent with { EffectivePrompt = intent.EffectivePrompt + new string('界', 1000) }, plan, HeaderFixture(plan.Native)));
        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public async Task ManagedModelIsNotAdvertisedWithoutConfiguredReadyEngine()
    {
        await using var host = await RunningServer.Start();
        using var provider = new LocalProviderClient(host.Origin, host.Token, modelId: ManagedModel.Id, images: new FixtureCodec());
        Assert.Equal("modelUnavailable", (await Assert.ThrowsAsync<ProviderException>(() => provider.DiscoverAsync())).Code);
    }

    [Fact]
    public async Task ManagedRetentionRemovesFinishedEngineCopyWithoutRepeatingInference()
    {
        var engine = new FixtureEngine();
        await using var host = await RunningServer.Start(configure: o => o with { LocalEngine = engine });
        using var provider = new LocalProviderClient(host.Origin, host.Token, modelId: ManagedModel.Id, images: new FixtureCodec());
        var scene = SceneFixture.Create();
        var plan = ResolutionPlanner.Plan(scene.Frame, scene.Frame, engine.Model.Resolution);
        var result = await provider.RenderPreparedAsync(scene, new Dictionary<string, byte[]> { ["reference.png"] = SceneFixture.Png },
            new() { RequestedOutput = scene.Frame, EffectivePrompt = PreservationRules.Objective }, plan, HeaderFixture(plan.Native));
        Assert.True(Directory.Exists(engine.Mirror));
        await host.Restart(beforeStart: root =>
        {
            string path = Path.Combine(root, result.Metadata.ResultId.ToString("N"), "job.json");
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            json["job"]!["createdAt"] = DateTimeOffset.UtcNow.AddDays(-2);
            File.WriteAllText(path, json.ToJsonString());
        });
        Assert.False(Directory.Exists(engine.Mirror));
        Assert.Equal(1, engine.Calls);
        using var client = host.Client();
        Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync($"/api/v1/jobs/{result.Metadata.ResultId:D}")).StatusCode);
    }

    // These are explicitly header-only protocol fixtures, not decoded model outputs.
    private static byte[] HeaderFixture(PixelSize size)
    {
        byte[] png = SceneFixture.Png.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), size.Width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20, 4), size.Height);
        return png;
    }
    private sealed class FixtureCodec : IImageProcessor
    {
        public byte[] PrepareReference(byte[] png, ResolutionPlan plan) => throw new Exception("Reviewed bytes must be reused.");
        public byte[] NormalizeResult(byte[] png, ResolutionPlan plan) => SceneFixture.Png;
    }
    private sealed class FixtureEngine : ILocalEngine
    {
        public ModelCapabilities Model => ManagedModel.Create("authored-fixture");
        public int Calls;
        public string? InputHash;
        public JobSubmission? Submission;
        public string? Mirror;
        public bool WaitForRelease, Stopped;
        public TaskCompletionSource<Guid> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<LocalEngineOutcome> EditAsync(Guid id, string directory, JobSubmission submission, Func<bool> cancelled, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            InputHash = ContentDigest.Sha256(await File.ReadAllBytesAsync(Path.Combine(directory, "reference.png"), ct));
            Submission = submission;
            Started.TrySetResult(id);
            if (WaitForRelease) await Release.Task.WaitAsync(ct);
            Stopped = true;
            Mirror = Path.Combine(Path.GetDirectoryName(directory)!, "engine-jobs", id.ToString("D"));
            Directory.CreateDirectory(Mirror);
            await File.WriteAllTextAsync(Path.Combine(Mirror, "state.json"), JsonSerializer.Serialize(new { jobId = id.ToString("D"), state = cancelled() ? "cancelled" : "completed" }), ct);
            await File.WriteAllTextAsync(Path.Combine(Mirror, "private-prompt.txt"), "owned fixture", ct);
            return cancelled() ? new(Cancelled: true) : new(HeaderFixture(submission.Plan.Native));
        }
    }

    private sealed class RunningServer : IAsyncDisposable
    {
        private WebApplication app = null!;
        private ReferenceServerOptions options = null!;
        public string Token => options.BearerToken;
        public Uri Origin => new($"http://127.0.0.1:{options.Port}/");

        public static async Task<RunningServer> Start(bool slow = false, Func<ReferenceServerOptions, ReferenceServerOptions>? configure = null)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var options = new ReferenceServerOptions
            {
                Port = port,
                StorageRoot = Path.Combine(Path.GetTempPath(), "VisoLoom-tests-" + Guid.NewGuid().ToString("N")),
                BearerToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                WorkerInterval = slow ? TimeSpan.FromMinutes(5) : TimeSpan.FromMilliseconds(10)
            };
            if (configure is not null)
                options = configure(options);
            var app = ReferenceApplication.Build([], options);
            await app.StartAsync();
            return new()
            {
                app = app,
                options = options
            };
        }

        public HttpClient Client()
        {
            var client = new HttpClient
            {
                BaseAddress = Origin
            };
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token);
            return client;
        }

        public async Task Restart(bool rotateToken = false, Action<string>? beforeStart = null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
            beforeStart?.Invoke(options.StorageRoot);
            if (rotateToken)
                options = options with { BearerToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) };
            app = ReferenceApplication.Build([], options);
            await app.StartAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(options.StorageRoot, true);
        }
    }
}

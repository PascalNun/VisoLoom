using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;
using VisoLoom.ReferenceServer;

var app = ReferenceApplication.Build(args);
await app.RunAsync();

namespace VisoLoom.ReferenceServer
{
    public static class ReferenceApplication
    {
        public static WebApplication Build(string[] args, ReferenceServerOptions? configuration = null)
        {
            var options = configuration ?? ReferenceServerOptions.FromEnvironment(); options.Validate();
            var builder = WebApplication.CreateBuilder(args);
            // No request-body, token, source-path or framework exception logs in normal operation.
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");
            builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = options.MaximumRequestBytes);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton<JobStore>();
            builder.Services.AddHostedService<ReferenceWorker>();
            var app = builder.Build();
            var store = app.Services.GetRequiredService<JobStore>();
            var caps = ReferenceCapabilities.Create(options.MaximumImageBytes, options.MaximumPixels, options.QueueCapacity, (int)options.Retention.TotalHours) with
            { MaximumUploadBytes = options.MaximumRequestBytes, MaximumJsonBytes = options.MaximumJsonBytes, MaximumParts = 3 };
            caps = caps with { Models = [caps.Models[0] with { Resolution = caps.Models[0].Resolution with { MaxEdge = options.MaximumEdge } }] };
            byte[] expectedTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(options.BearerToken));
            app.Use(async (context, next) =>
            {
                try
                {
                    if (context.Request.Headers.ContainsKey("Origin") || context.Request.Headers.ContainsKey("Forwarded") || context.Request.Headers.ContainsKey("X-Forwarded-Host") ||
                        !IPAddress.TryParse(context.Request.Host.Host.Trim('[', ']'), out var host) || !IPAddress.IsLoopback(host) ||
                        context.Request.Host.Port != options.Port || context.Connection.RemoteIpAddress is { } remote && !IPAddress.IsLoopback(remote))
                        throw new ProtocolFailure(403, "policyDenied", "The development server accepts direct loopback requests only.");
                    var authorization = context.Request.Headers.Authorization.ToString();
                    if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) || authorization.Length > 4096 ||
                        !CryptographicOperations.FixedTimeEquals(expectedTokenHash, SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..]))))
                        throw new ProtocolFailure(401, "authenticationRequired", "A valid session token is required.");
                    context.Response.Headers.CacheControl = "no-store";
                    await next(context);
                }
                catch (ProtocolFailure failure) { await Problem(context, failure.Status, failure.Code, failure.Message); }
                catch (JsonException) { await Problem(context, 400, "invalidJson", "A JSON part is malformed or missing required properties."); }
                catch (BadHttpRequestException ex) { await Problem(context, ex.StatusCode, "invalidRequest", "The HTTP request is malformed or exceeds its limits."); }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await Problem(context, 500, "storageFailure", "The reference server could not access its private storage."); }
                catch (Exception) { await Problem(context, 500, "internalError", "The reference server could not complete the request."); }
            });
            app.MapGet("/api/v1/capabilities", () => Results.Json(caps, WireJson.Options));
            app.MapPost("/api/v1/jobs", async (HttpContext context) =>
            {
                var key = context.Request.Headers["Idempotency-Key"].ToString();
                if (key.Length is < 1 or > 128 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
                    throw new ProtocolFailure(400, "invalidIdempotencyKey", "A bounded ASCII idempotency key is required.");
                var parts = await MultipartSubmission.ReadAsync(context.Request, options, context.RequestAborted);
                var request = SubmissionValidator.Deserialize<JobSubmission>(parts.Request);
                var scene = SubmissionValidator.Deserialize<SceneManifest>(parts.Scene);
                if (request.SceneDigest != ContentDigest.Sha256(parts.Scene)) throw new ProtocolFailure(422, "invalidSceneDigest", "The scene digest does not match the submitted JSON.");
                var issues = SubmissionValidator.Validate(request, scene, parts.Reference, caps);
                if (issues.Count != 0)
                {
                    var issue = issues[0];
                    throw new ProtocolFailure(issue.Code == "capabilityChanged" ? 409 : 422, issue.Code, issue.Message);
                }
                var digest = ContentDigest.Submission(parts.Request, parts.Scene, new Dictionary<string, byte[]> { ["reference.png"] = parts.Reference });
                var job = store.Submit(key, digest, request, scene, parts);
                context.Response.Headers.Location = $"/api/v1/jobs/{job.Id:D}";
                context.Response.Headers.RetryAfter = "1";
                return Results.Json(job, WireJson.Options, statusCode: 202);
            });
            app.MapGet("/api/v1/jobs/{id:guid}", (Guid id) => Results.Json(store.Get(id), WireJson.Options));
            app.MapPost("/api/v1/jobs/{id:guid}/cancel", (Guid id) => Results.Json(store.Cancel(id), WireJson.Options));
            app.MapGet("/api/v1/jobs/{id:guid}/outputs/{outputId}", (Guid id, string outputId) => Results.File(store.OutputPath(id, outputId), "image/png", enableRangeProcessing: false));
            app.MapFallback(() => Results.Json(new ProblemResponse { Status = 404, Title = "Not found", Code = "notFound", CorrelationId = Guid.NewGuid().ToString("N") }, WireJson.Options, "application/problem+json", 404));
            return app;
        }

        private static async Task Problem(HttpContext context, int status, string code, string message)
        {
            if (context.Response.HasStarted) { context.Abort(); return; }
            context.Response.StatusCode = status; context.Response.ContentType = "application/problem+json";
            if (status == 401) context.Response.Headers.WWWAuthenticate = "Bearer";
            if (status == 429) context.Response.Headers.RetryAfter = "1";
            await JsonSerializer.SerializeAsync(context.Response.Body, new ProblemResponse { Status = status, Title = message, Code = code, CorrelationId = context.TraceIdentifier }, WireJson.Options, context.RequestAborted);
        }
    }
}

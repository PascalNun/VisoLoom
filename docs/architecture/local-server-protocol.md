# Local and office HTTP protocol proposal

Status: draft for approval. Proposed protocol `1.0` under `/api/v1`. No server is running or implemented.

The protocol is independent of .NET, Python, ComfyUI, FLUX, or deployment hardware. Use JSON control messages and multipart binary assets, with an asynchronous job resource. The reference implementation can use ASP.NET Core; a later Python inference service can implement the same wire contract.

## Endpoints

| Request | Meaning / response |
| --- | --- |
| `GET /api/v1/capabilities` | `200`: protocol/schema versions, capability revision, models, pass usages, size/byte limits, queue limit, auth modes, retention and restart behavior. |
| `POST /api/v1/jobs` | Multipart `request` JSON, `scene` JSON, and manifest-listed raster parts. Validated accepted job: `202`, job representation and relative `Location`. |
| `GET /api/v1/jobs/{id}` | Job state, monotonic revision, optional progress, timestamps, result metadata or typed failure. |
| `POST /api/v1/jobs/{id}/cancel` | Idempotent cancellation request. `202` while pending; `200` with current terminal representation if already terminal. |
| `GET /api/v1/jobs/{id}/outputs/{outputId}` | Authorized streaming PNG after completion, with length/hash and validated local identifier. No user-supplied filesystem path. |

Output deletion is a separate future feature; cancellation does not erase history. Unknown job returns `404`; an expired job known to the retention index returns `410`.

Submission JSON contains protocol version, client request UUID, model/version, capability revision, render intent, planned native and final dimensions/transforms, scene schema version and content digest. `scene.json` contains the neutral unpadded capture. The server rejects incompatible plans instead of silently renegotiating. A later server with its own adapter must obey the submitted plan.

## Idempotency, retries, and races

Require an `Idempotency-Key` for job creation, scoped to the authenticated principal. Compute a request digest from the exact submitted JSON part bytes and a deterministic sorted sequence of asset name/hash/length tuples. Reusing a key with the same digest returns the same job; different content returns `409`. Retain the mapping for at least the advertised job retention interval. Persist accepted jobs and keys before acknowledging acceptance.

If the upload response is lost, the client retries the same request with the same key; it never starts a second paid operation merely to recover status. The reference server has no paid side effect. A future inference server must persist downstream submission handles or mark an ambiguous external submission for reconciliation rather than blindly replay it.

Server states: `queued -> running -> completed | failed | cancelled`. Cancellation request is tracked separately from terminal state. Serialize worker transitions per job. If completion wins the cancellation race, return completed; never relabel a saved successful output as cancelled. Client states additionally include preparing, capturing, uploading, downloading, and postprocessing. A timeout/disconnection is `connectionLost` locally, not proof that the server failed. See [domain model](domain-model.md).

Initial reference-server proposal: one worker, bounded queue, configurable upload/capture limits, private per-user disk storage, and a 24-hour development retention default advertised by capabilities. On restart, incomplete accepted jobs become `failed/serverRestarted`; completed jobs and idempotency mappings remain readable until expiry. This is a deliberate development contract, not a resilient enterprise scheduler.

Poll with bounded backoff and jitter; respect `Retry-After`. Cancel local polling promptly but represent remote status honestly. Never apply automatic POST retries to a cloud API without a documented safe deduplication strategy.

## Validation and errors

Bound streamed request bytes, JSON size/depth, part count, individual image bytes, total decoded pixels, and queue length. Validate manifests and assets before accepting the job. Use server-generated storage names and exclusive job directories; no arbitrary URLs, filesystem paths, commands, or workflow execution in requests. PNG files are never executed. First reference output is a byte-for-byte copy, with hash/dimension checks; the service does not need to decode or re-encode images for passthrough. Returned images must be decoded by a proven codec before UI display.

Use `application/problem+json` with HTTP status and a stable VisoLoom error code, request correlation ID, safe message, and optional field errors. Proposed codes include `unsupportedVersion`, `capabilityChanged`, `missingPass`, `invalidAsset`, `unsupportedResolution`, `queueFull`, `authenticationRequired`, `policyDenied`, and `serverRestarted`. Do not include prompts, secrets, stack traces, or absolute paths. Standard problem-details format: [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457.html).

Return `400` for malformed input, `401/403` for authentication/authorization, `409` for changed capability/idempotency conflict, `413` for size limits, `415` for unsupported MIME, `422` for valid but unsupported scene/plan, `429` for full queue, and `5xx` for service failure. Error responses must match the actual HTTP status.

## Transport security

Reference service binds loopback only and uses a locally generated token by default. Token acquisition is developer setup, never hardcoded source. An explicit unauthenticated development option must remain loopback-only, validate Host, reject browser Origin requests, and disable cross-origin access. It cannot be used for office deployment. Office endpoints require authenticated HTTPS and a configured allowlist; certificate errors are not bypassed.

Use the same policy on discovery, submission, polling and outputs. Do not forward Authorization or provider API keys to a redirect, signed output URL, or new host. Local protocol output links must be relative to the configured origin. Cloud output URLs require an adapter-specific trusted-origin policy and no credential forwarding. Server-side model/network routing is controlled by the operator, never a client-supplied fetch URL.

Use a proven HTTP server and streaming APIs rather than implementing HTTP or multipart parsing. [ASP.NET Core upload guidance](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0).

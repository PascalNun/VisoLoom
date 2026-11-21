# Local and office HTTP protocol proposal

Status: protocol `1.0` implemented under `/api/v1` for the loopback reference server. Office deployment remains deferred. See [development instructions](../../DEVELOPMENT.md) and [implementation review](../development/phase-4-review.md).

The protocol is independent of .NET, Python, ComfyUI, FLUX, or deployment hardware. Use JSON control messages and multipart binary assets, with an asynchronous job resource. The reference implementation can use ASP.NET Core; a later Python inference service can implement the same wire contract.

The experimental managed connection adds a prepared FLUX model alongside passthrough. A managed submission's optional `preparedReference` describes the uploaded model PNG (size, byte count and SHA256); the Scene Manifest still describes the original Rhino capture. The server validates the complete plan and prepared reference before accepting a job. `ResolutionPlan.sampling` optionally records subpixel content bounds at one uniform scale, only for capabilities that explicitly allow fractional sampling. The engine receives the prepared image and concise effective prompt, not scene JSON. Managed output uses the exact job-relative `/outputs/result` path; passthrough keeps `/outputs/reference`. Cancellation of a running managed edit is not terminal until worker exit is confirmed. See [ADR 0018](../adr/0018-prepared-local-model-and-framing.md).

## Endpoints

| Request | Meaning / response |
| --- | --- |
| `GET /api/v1/capabilities` | `200`: protocol/schema versions, capability revision, models, pass usages, size/byte limits, queue limit, auth modes, retention and restart behavior. |
| `POST /api/v1/jobs` | Multipart `request` JSON, `scene` JSON, and manifest-listed raster parts. Validated accepted job: `202`, job representation and relative `Location`. |
| `GET /api/v1/jobs/{id}` | Job state, monotonic revision, timestamps, result metadata or typed failure; for a running local edit also `stepProgress` (best-effort engine phase, optional sampler step of steps, and the runner's seconds-remaining estimate). Terminal responses never carry `stepProgress`. |
| `POST /api/v1/jobs/{id}/cancel` | Idempotent cancellation request. `202` while pending; `200` with current terminal representation if already terminal. |
| `GET /api/v1/jobs/{id}/outputs/{outputId}` | Authorized streaming PNG after completion, with length/hash and validated local identifier. No user-supplied filesystem path. |

Output deletion is a separate future feature; cancellation does not erase history. Unknown job returns `404`; an expired job known to the retention index returns `410`.

Submission JSON contains protocol version, client request UUID, model/version, capability revision, render intent, planned native and final dimensions/transforms, scene schema version and content digest. `scene.json` contains the neutral unpadded capture. The server rejects incompatible plans instead of silently renegotiating. A later server with its own adapter must obey the submitted plan.

## Idempotency, retries, and races

Require an `Idempotency-Key` for job creation, scoped to the authenticated principal. Compute a request digest from the exact submitted JSON part bytes and a deterministic sorted sequence of asset name/hash/length tuples. Reusing a key with the same digest returns the same job; different content returns `409`. Retain the mapping for at least the advertised job retention interval. Persist accepted jobs and keys before acknowledging acceptance.

If the upload response is lost, the client retries the same request with the same key; it never starts a second paid operation merely to recover status. The reference server has no paid side effect. A future inference server must persist downstream submission handles or mark an ambiguous external submission for reconciliation rather than blindly replay it.

Server states: `queued -> running -> completed | failed | cancelled`. Cancellation request is tracked separately from terminal state. Serialize worker transitions per job. If completion wins the cancellation race, return completed; never relabel a saved successful output as cancelled. Client states additionally include preparing, capturing, uploading, downloading, and postprocessing. A timeout/disconnection is `connectionLost` locally, not proof that the server failed. See [domain model](domain-model.md).

Initial reference-server proposal: one worker, bounded queue, configurable upload/capture limits, private per-user disk storage, and a 24-hour development retention default advertised by capabilities. On restart, incomplete accepted jobs become `failed/serverRestarted`; completed jobs and idempotency mappings remain readable until expiry. This is a deliberate development contract, not a resilient enterprise scheduler.

The development client polls at one second plus jitter. Future office/inference adapters should use bounded backoff and respect `Retry-After`. Cancel local polling promptly but represent remote status honestly. Never apply automatic POST retries to a cloud API without a documented safe deduplication strategy.

## Validation and errors

Bound streamed request bytes, JSON size/depth, part count, individual image bytes, total decoded pixels, and queue length. Validate manifests and assets before accepting the job. Use server-generated storage names and exclusive job directories; no arbitrary URLs, filesystem paths, commands, or workflow execution in requests. PNG files are never executed. First reference output is a byte-for-byte copy, with hash/dimension checks; the service does not need to decode or re-encode images for passthrough. Returned images must be decoded by a proven codec before UI display.

Use `application/problem+json` with HTTP status and a stable VisoLoom error code, request correlation ID, safe message, and optional field errors. Proposed codes include `unsupportedVersion`, `capabilityChanged`, `missingPass`, `invalidAsset`, `unsupportedResolution`, `queueFull`, `authenticationRequired`, `policyDenied`, and `serverRestarted`. Do not include prompts, secrets, stack traces, or absolute paths. Standard problem-details format: [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457.html).

Return `400` for malformed input, `401/403` for authentication/authorization, `409` for changed capability/idempotency conflict, `413` for size limits, `415` for unsupported MIME, `422` for valid but unsupported scene/plan, `429` for full queue, and `5xx` for service failure. Error responses must match the actual HTTP status.

## Transport security

The reference service binds loopback only and requires a locally generated token supplied through its environment. Token acquisition is developer setup, never hardcoded source. There is no unauthenticated option. Validate Host, reject browser Origin requests and disable cross-origin access. Future office endpoints require authenticated HTTPS and a configured allowlist; certificate errors must not be bypassed.

Use the same policy on discovery, submission, polling and outputs. Do not forward Authorization or provider API keys to a redirect, signed output URL, or new host. Local protocol output links must be relative to the configured origin. Cloud output URLs require an adapter-specific trusted-origin policy and no credential forwarding. Server-side model/network routing is controlled by the operator, never a client-supplied fetch URL.

Use a proven HTTP server and streaming APIs rather than implementing HTTP or multipart parsing. [ASP.NET Core upload guidance](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0).

## Implemented development profile (2026-10-11)

The accepted general protocol is deliberately narrowed for the first slice:

- Literal IPv4 loopback port 5179 only; required environment-supplied random bearer token. No unauthenticated switch or office listener. One local operator identity; token rotation does not create a separate billing principal. Multi-user office authorization is deferred.
- Exactly `request` (`application/json`), `scene` (`application/json`), `reference.png` (`image/png`) multipart parts. No archive, URL fetching or geometry transport. A maximum of eight connections and two simultaneous body readers bound upload memory; four accepted unfinished jobs, one worker.
- Defaults: 36 MiB whole request, 32 MiB PNG, 1 MiB each JSON part, 8,192-pixel maximum edge and 16,777,216 pixels. Limits are advertised. First-slice clients accept only exact passthrough plans.
- Digest is SHA256 over length-prefixed request bytes, scene bytes and sorted asset tuples. Every component uses an unsigned-compatible positive 64-bit big-endian length prefix; each tuple contains UTF-8 name, binary SHA256, and invariant decimal ASCII byte length. This avoids concatenation ambiguity. The snapshot UUID is the reference client's idempotency key, with a stable request UUID equal to that snapshot UUID. One development job per capture; different bytes for that capture conflict rather than duplicate.
- Output ID is `reference`, returned at the exact relative path `/api/v1/jobs/{UUID}/outputs/reference`. The output is the original stored PNG, not a re-encode. Server validation checks signature/IHDR/IEND metadata, dimensions and hash; this is not a full PNG decoder. The Rhino UI uses its established codec before display.
- Cancellation is serialized with completion. A queued cancellation is immediately terminal; an already committed result wins the race. Polling is one second plus jitter. The client makes one immediate safe retry after a lost submission response; its Reconnect action reuses the captured request during the same Rhino session. Reopening client history after Rhino restart is deferred.
- Server restart marks incomplete persisted jobs failed and retains completed outputs and keys. Retention removes scene/request/image files after 24 hours; a minimal expired-ID/key/digest record is retained another 24 hours, then unknown IDs become 404.

Schemas and records are in Core/Protocol; non-JSON payloads are never interpreted as commands. Future inference backends must implement a new capability/model revision and cannot silently turn this passthrough model into a generative model.

# VisoLoom implementation review

Reviewed 2026-10-11. The phases are delivery checkpoints, not additional approval gates. The approved Phase 0 review and ADRs remain authoritative. Repository: [PascalNun/VisoLoom](https://github.com/PascalNun/VisoLoom), **private**, no project LICENSE, no public package or release.

## Working functionality

- Buildable .NET solution with host-independent Core, versioned Protocol, provider boundary, Rhino plug-in and optional ASP.NET reference service. Exact direct versions, transitive lockfiles, documented dependency licenses and Windows/macOS CI.
- Reference Scene Package records, structural JSON Schema, semantic validation, authored PNG/JSON fixture, camera/frustum/frame invariants, safe logical asset names and hash verification. No geometry or texture payloads.
- Resolution planning for exact, dimension-grid and fixed-canvas constraints, preserving complete-frame aspect and recorded transforms. The reference adapter accepts exact plans only; it never silently crops, stretches or downsizes.
- Real HTTP round trip: discovery, authenticated bounded multipart upload, persistent asynchronous job, idempotency/reconnect, cancellation, restart recovery, retention and byte-identical PNG return. The client validates same-origin output paths, MIME, bytes, hash and dimensions; rejects redirects. No image model runs.
- Compiled native Eto panel and commands `VisoLoom` / `VisoLoomRender`: current view, resolution, explicit local destination, automatic session connection and manual override, **Test capture**, cancellation/reconnect, source/result preview and atomic PNG export. Jobs live outside panel instances and artifacts outside `.3dm`.
- Compiled UI-thread extraction reads camera, eligible object/layer/material metadata and clipping participation, including hidden clipping widgets. It does not write cameras, geometry, materials or visibility. Scene/view changes observed during capture invalidate the snapshot.
- Seven editable preset documents including the supplied Competition Sketch text; preservation-aware prompt composition and material-description precedence helpers for later adapters.
- Human-readable repository map, contributor starting path and expanded code formatting without new frameworks or folders. The [roadmap](../roadmap.md) prioritizes UX acceptance and plans a purpose-built library of recognizable, optimized Rhino assets with geometry, materials and additional representation metadata.
- Double-click Mac [test launcher](../../Test%20VisoLoom.command): build current code, refresh a Rhino 8 development bundle, start and verify the authenticated local service, enable a same-OS-user credential handoff and open Rhino without a token paste. `--check` runs the existing automated suite; `--headless` permits startup checks without UI access. Changed plug-in code requires a Rhino restart. See [quick-test steps](../../DEVELOPMENT.md#quick-test-on-your-mac).

**Evidence boundary:** Core and HTTP behavior have executable test evidence. User-provided Mac screenshots confirm plug-in loading, command registration and panel opening, followed by a manually connected Wireframe perspective round trip. On 2026-10-11 the user also confirmed automatic connection without token entry; the accompanying screenshot shows a successful standard Rendered perspective capture at 2358 × 1810 pixels, both previews and reported byte equality. Labels are left-aligned and the primary action and compact Cancel button are accessible at the shown width. Export, narrow-width/high-DPI coverage, no-mutation checks and the full host acceptance matrix remain open.

## Stubs and deferred work

Black Forest Labs, OpenAI, Google and xAI are unavailable provider descriptors. No cloud credentials, SDKs or outbound cloud requests are implemented. Auxiliary passes (edges/depth/normals/material/semantic/object IDs/section mask), inference backends, image resampling, semantic correction UI and mapping libraries remain deferred. Mode and prompt widgets are disabled in the capture test.

The [model-selection UX](../ux/information-architecture.md#model-choice-and-connection-settings) now specifies one-time connection setup and a direct Model dropdown with visible destination, retained intent and per-job model provenance. It is an interaction target; multi-connection settings and switching between real providers are not implemented.

Provider capabilities and result provenance are neutral Core records; HTTP server job records remain inside Protocol. A future synchronous image API need not fabricate a server job.

The credential interface and explicit session-only store exist; Keychain/Credential Manager persistence remains unimplemented and must pass native testing before adoption. Office HTTPS policy/deployment, multi-user authorization, reopened history browsing and durable client reconnection across a Rhino restart are also deferred.

The asset browser and asset production are planned only. Assets should already look close to their intended appearance in Rhino, then receive image-model refinement. Authoring/sourcing, possible AI-assisted mesh creation, material preparation, optimization and library expansion require a separate milestone. No third-party asset download or geometry-generation tool has been added.

The [material collection](../roadmap.md#rhino-material-collection) is also planned only: ordinary editable Rhino materials and textures, with no requirement to use the VisoLoom catalogue. The [input strategy](../architecture/proposed-architecture.md#reference-image-and-visible-intent) now explicitly prioritizes the chosen Rhino appearance, including the user's custom rendered/AO/edge style, appropriate texture scale and deliberate lighting. This documents the intended workflow; it does not enable custom modes or full lighting/PBR extraction.

## Changed assumptions and bounded refinements

- The user's clarified target is the **Rhino 8 series**, replacing the initial 8.21 minimum. The plug-in and shared libraries now target .NET 7 against original stable RhinoCommon **8.0.23304.9001**, for Rhino's default .NET 7 and later .NET 8 hosts. Current APIs compile against the original SDK; no later service release is required by this slice. Native compatibility remains a validation target, not an exhaustive claim. The installed Rhino 8.16 was left unchanged. See [ADR 0010](../adr/0010-rhino-8-series-compatibility.md).
- The local test launcher now connects automatically through an explicitly enabled named pipe restricted to the same OS user at both ends. Tokens stay in memory and refresh before new captures; manual connections are retained. Browser HTTP cannot bootstrap credentials. See [ADR 0011](../adr/0011-automatic-local-development-connection.md).
- The reference service requires an environment-supplied random token. There is no unauthenticated mode. It binds literal loopback only; office deployment is not implicitly enabled by an HTTP URL field.
- The first UI calls the operation **Test capture**, explicitly labels returned images unchanged, and disables generative controls. A successful image round trip is not a successful generative representation.
- Initial host scope is model-space perspective/parallel in Wireframe/Shaded/Rendered. Two-point perspective, layout/details and other display modes are rejected pending host validation.
- The reference profile uses one job per scene UUID and immutable request bytes for recovery. Future multiple render attempts against one scene need an explicit attempt identity in their adapter contract.

## Known limitations and risks

- Host validation is incomplete: Wireframe and standard Rendered perspective captures have been observed on Mac, along with automatic connection and corrected layout at the shown width. Broader capture compatibility, narrow-width/high-DPI behavior, additional Eto interactions and no-mutation guarantees remain unverified. Synchronous capture/encoding can briefly block Rhino and needs measurement on architectural fixtures.
- Material metadata is approximate eligible-scene context, not proven pixel visibility. Legacy diffuse colors are available; complex render-content/PBR extraction, texture-mapping metadata, nested instances and subobject assignments remain unresolved. Visible texture appearance may already be present in the reference pixels. Complete lighting/display settings are not extracted, and custom modes are currently rejected. Named View association is not inferred or recreated.
- Auxiliary passes are not available. The PNG server checks bounded header metadata and hashes; it is not a full image decoder. The host codec must validate an image before display.
- Server state is one-process disk persistence, not an enterprise scheduler. Restart intentionally fails unfinished accepted work. Raw input/metadata can be confidential even though only loopback is allowed. Retention is bounded; Windows per-user ACL inheritance needs deployment validation.
- Camera/reference preservation is a deterministic input requirement. Future generative geometry drift remains a defect to measure and reduce, never an accepted “creative” change by default. No model fidelity benchmark has been run.
- Document-close, slow/disconnected service, disk-full, native save dialogs, corrupt-store recovery and cross-platform host behavior require the manual acceptance matrix. Reconnect is currently a same-session action; there is no full history-management UI.

## Blockers

Native installation, panel opening, automatic connection and successful Wireframe/Rendered perspective round trips have been observed on Mac. Export, service restart with Rhino kept open, narrow-panel layout and broader host acceptance remain open. The development machine's **Rhino 8.16** can now be used without an update; also exercise an earliest available and current Rhino 8 host, including a Windows host for its UI/capture matrix. Windows' optional legacy .NET Framework mode is outside the current build. No source-build or local HTTP test blocker remains.

McNeel's pinned NuGet package does not declare SDK license terms. Host SDK assemblies are excluded from output, but applicable SDK/distribution terms still need confirmation before releasing binaries. Licensing for VisoLoom itself remains intentionally undecided.

## Test status

Release build: **zero warnings, zero errors**. Core tests run on .NET 8; service/provider tests run on .NET 10 with real loopback HTTP. Tests cover framing, fixed/grid/ratio limits, schema/version/reference checks, malformed JSON, prompt preservation, mapping conflicts, origin policy, exact PNG round trip, auth/Host/Origin rejection, retry/idempotency, reconnect, cancellation, restart, queue/byte limits and expiry. Authored presets and sample Scene Package load through the same wire contracts. No billable API tests or confidential fixtures.

**24 Core tests and 19 protocol/provider integration tests pass (43 total) locally and in macOS/Windows CI, with zero local build warnings/errors.** The [automatic-connection CI run](https://github.com/PascalNun/VisoLoom/actions/runs/38167463771) passed both platforms at `f1d236a`. The new tests cover automatic handoff and authenticated capture, fresh credentials after restart, invalid/truncated payloads, absent/stalled services and caller cancellation. Cross-account OS access was not exercised; the implementation relies on .NET peer checks and private socket permissions. The prior 36-test suite passed macOS/Windows CI with the compatibility correction. The [compatibility source CI run](https://github.com/PascalNun/VisoLoom/actions/runs/38155085522) passed both platforms for commit `a7e085b`. The local Release build passed with zero warnings/errors. All plug-in source also compiled against the installed 8.16 SDK. A separate probe using Rhino 8.16's actual bundled **.NET 7.0.0** passed discovery, byte-identical PNG round trip and idempotent replay. This proves shared-client behavior under that runtime, not native Rhino capture or UI. The new automatic handoff also passed an isolated process probe under that actual .NET 7.0.0 runtime: automatic discovery, authenticated byte-identical PNG return and anonymous rejection, repeated after restarting the service with a fresh token.

The earlier [source CI run](https://github.com/PascalNun/VisoLoom/actions/runs/38153536133) passed both platforms at `a127ac9` before the compatibility correction. That readability pass preserved every syntax token across all 26 C# source files; no behavior or dependency was added by it.

An earlier CI run exposed a request-draining issue for oversized bodies; the server now closes rejected connections explicitly. This was fixed without weakening the byte-limit test. Documentation-only handoff commits do not trigger the source workflow.

NuGet vulnerability enumeration including transitives reported no known advisories at the time of review. Plug-in output is checked for accidentally bundled Rhino/Eto/Drawing assemblies. Machine-readable schema was parsed; semantic validation is exercised in C#, rather than introducing an extra runtime schema engine.

Local launcher checks passed: invocation from outside the checkout, build/36-test mode, six-file development-bundle integrity, authenticated service readiness, unauthenticated request rejection, occupied-port refusal and service cleanup on interruption. The former clipboard flow has been replaced by the local handoff; new launcher/native acceptance remains open. The development service used for these checks was stopped afterward.

The screenshot-driven panel correction passes the local Release build with zero warnings/errors and all 36 tests. A follow-up Mac screenshot confirms the connection button and missing-token instruction are accessible; it exposed remaining host label alignment and button scaling problems. The follow-up code applies left alignment after cascading styles and explicitly scales the primary action. The latest screenshot confirms corrected alignment/action sizing and populated previews during a completed Rendered capture at the shown width. Native checks at 300–420 logical pixels and the manual connection dialog remain open. No custom display-mode support or provider integration was added.

## Recommended next milestone

Complete the supported-host capture and **UX acceptance** milestone first: native panel installation, first-run connection, clear view/resolution readouts, source/result comparison, cancellation/reconnection and save behavior. Exercise authored architectural fixtures on Apple Silicon and Windows, inspect hidden/finite clipping and material cases, verify no document mutations, and measure capture pauses. Fix friction and metadata gaps before adding more providers. The [host checklist](plugin-smoke-test.md) defines observable pass/fail evidence.

Then add **one real reference-image editing adapter**, with explicit cloud opt-in, OS-backed credentials, payload disclosure, supported-size negotiation and an architectural fidelity benchmark. Keep openings, stairs, section voids, proportions and border landmarks as measured criteria separate from style preference.

## Recommended first real image integration

Use the **OpenAI Image API edits endpoint**, initially evaluating the dated **`gpt-image-2.5-sunburst-2026-09-08`** snapshot. Current official documentation positions Sunburst for editing precision and supports direct reference-image edits. That makes it a practical first candidate for this input/output architecture without installing a local ML runtime. This is a recommendation to benchmark, not evidence it preserves architectural geometry better than alternatives. [Image generation guide](https://developers.openai.com/api/docs/guides/image-generation), [edits endpoint](https://developers.openai.com/api/reference/resources/images/methods/edit).

Confirm account availability and current limits during implementation; use one source image and one output initially. Reject unsupported plans instead of silently reframing. Keep source/generated comparison and record geometric deviations. If its benchmark is inadequate, the next useful comparison is a controllable local/office pipeline using aligned structural passes, after those passes themselves are validated. Do not implement every cloud family in parallel.

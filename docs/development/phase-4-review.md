# VisoLoom implementation review

Reviewed 2026-10-11. The phases are delivery checkpoints, not additional approval gates. The approved Phase 0 review and ADRs remain authoritative. Repository: [PascalNun/VisoLoom](https://github.com/PascalNun/VisoLoom), **private**, no project LICENSE, no public package or release.

## Working functionality

- Buildable .NET solution with host-independent Core, versioned Protocol, provider boundary, Rhino plug-in and optional ASP.NET reference service. Exact direct versions, transitive lockfiles, documented dependency licenses and Windows/macOS CI.
- Reference Scene Package records, structural JSON Schema, semantic validation, authored PNG/JSON fixture, camera/frustum/frame invariants, safe logical asset names and hash verification. No geometry or texture payloads.
- Resolution planning for exact, dimension-grid and fixed-canvas constraints, preserving complete-frame aspect and recorded transforms. The reference adapter accepts exact plans only; it never silently crops, stretches or downsizes.
- Real HTTP round trip: discovery, authenticated bounded multipart upload, persistent asynchronous job, idempotency/reconnect, cancellation, restart recovery, retention and byte-identical PNG return. The client validates same-origin output paths, MIME, bytes, hash and dimensions; rejects redirects. No image model runs.
- Compiled native Eto panel and commands `VisoLoom` / `VisoLoomRender`: current view, resolution, explicit local destination, session token, **Test capture**, cancellation/reconnect, source/result preview and atomic PNG export. Jobs live outside panel instances and artifacts outside `.3dm`.
- Compiled UI-thread extraction reads camera, eligible object/layer/material metadata and clipping participation, including hidden clipping widgets. It does not write cameras, geometry, materials or visibility. Scene/view changes observed during capture invalidate the snapshot.
- Seven editable preset documents including the supplied Competition Sketch text; preservation-aware prompt composition and material-description precedence helpers for later adapters.
- Human-readable repository map, contributor starting path and expanded code formatting without new frameworks or folders. The [roadmap](../roadmap.md) prioritizes UX acceptance and plans a purpose-built library of recognizable, optimized Rhino assets with geometry, materials and additional representation metadata.

**Evidence boundary:** the Core and HTTP behavior has executable test evidence. The Rhino code has compilation evidence only. A complete in-Rhino end-to-end run and visual/UX acceptance have not been performed.

## Stubs and deferred work

Black Forest Labs, OpenAI, Google and xAI are unavailable provider descriptors. No cloud credentials, SDKs or outbound cloud requests are implemented. Auxiliary passes (edges/depth/normals/material/semantic/object IDs/section mask), inference backends, image resampling, semantic correction UI and mapping libraries remain deferred. Mode and prompt widgets are disabled in the capture test.

Provider capabilities and result provenance are neutral Core records; HTTP server job records remain inside Protocol. A future synchronous image API need not fabricate a server job.

The credential interface and explicit session-only store exist; Keychain/Credential Manager persistence remains unimplemented and must pass native testing before adoption. Office HTTPS policy/deployment, multi-user authorization, reopened history browsing and durable client reconnection across a Rhino restart are also deferred.

The asset browser and asset production are planned only. Assets should already look close to their intended appearance in Rhino, then receive image-model refinement. Authoring/sourcing, possible AI-assisted mesh creation, material preparation, optimization and library expansion require a separate milestone. No third-party asset download or geometry-generation tool has been added.

## Changed assumptions and bounded refinements

- Stable RhinoCommon **8.21.25188.17001**, minimum Rhino **8.21 + .NET 8**, refines the proposed 8.20+ baseline. NuGet's 8.20 packages were prereleases. The installed Rhino 8.16 was left unchanged.
- The reference service requires an environment-supplied random token. There is no unauthenticated mode. It binds literal loopback only; office deployment is not implicitly enabled by an HTTP URL field.
- The first UI calls the operation **Test capture**, explicitly labels returned images unchanged, and disables generative controls. A successful image round trip is not a successful generative representation.
- Initial host scope is model-space perspective/parallel in Wireframe/Shaded/Rendered. Two-point perspective, layout/details and other display modes are rejected pending host validation.
- The reference profile uses one job per scene UUID and immutable request bytes for recovery. Future multiple render attempts against one scene need an explicit attempt identity in their adapter contract.

## Known limitations and risks

- No host validation means capture compatibility, high-DPI pixel interpretation, preview layout, Eto interaction, drawing codec behavior and no-mutation guarantees have not yet been demonstrated in Rhino. Synchronous capture/encoding can briefly block Rhino and needs measurement on architectural fixtures.
- Material metadata is approximate eligible-scene context, not proven pixel visibility. Legacy diffuse colors are available; complex render content, PBR, texture appearance, nested instances and subobject assignments remain unresolved. Named View association is not inferred or recreated.
- Auxiliary passes are not available. The PNG server checks bounded header metadata and hashes; it is not a full image decoder. The host codec must validate an image before display.
- Server state is one-process disk persistence, not an enterprise scheduler. Restart intentionally fails unfinished accepted work. Raw input/metadata can be confidential even though only loopback is allowed. Retention is bounded; Windows per-user ACL inheritance needs deployment validation.
- Camera/reference preservation is a deterministic input requirement. Future generative geometry drift remains a defect to measure and reduce, never an accepted “creative” change by default. No model fidelity benchmark has been run.
- Document-close, slow/disconnected service, disk-full, native save dialogs, corrupt-store recovery and cross-platform host behavior require the manual acceptance matrix. Reconnect is currently a same-session action; there is no full history-management UI.

## Blockers

The development machine has **Rhino 8.16**, below the implementation baseline. Supported Rhino 8.21+ with .NET 8 is required to run the native acceptance checks. A Windows Rhino host is also needed for its host/UI matrix. No source-build or local HTTP test blocker remains.

McNeel's pinned NuGet package does not declare SDK license terms. Host SDK assemblies are excluded from output, but applicable SDK/distribution terms still need confirmation before releasing binaries. Licensing for VisoLoom itself remains intentionally undecided.

## Test status

Release build: **zero warnings, zero errors**. Core tests run on .NET 8; service/provider tests run on .NET 10 with real loopback HTTP. Tests cover framing, fixed/grid/ratio limits, schema/version/reference checks, malformed JSON, prompt preservation, mapping conflicts, origin policy, exact PNG round trip, auth/Host/Origin rejection, retry/idempotency, reconnect, cancellation, restart, queue/byte limits and expiry. Authored presets and sample Scene Package load through the same wire contracts. No billable API tests or confidential fixtures.

**24 Core tests and 12 HTTP integration tests pass (36 total), locally and in macOS/Windows CI.** The [final source CI run](https://github.com/PascalNun/VisoLoom/actions/runs/38153536133) passed both platforms for commit `a127ac9`. The final local Release build also passed with zero warnings/errors. The readability pass was checked to preserve every syntax token across all 26 C# source files. No behavior or dependency was added by that pass.

An earlier CI run exposed a request-draining issue for oversized bodies; the server now closes rejected connections explicitly. This was fixed without weakening the byte-limit test. Documentation-only handoff commits do not trigger the source workflow.

NuGet vulnerability enumeration including transitives reported no known advisories at the time of review. Plug-in output is checked for accidentally bundled Rhino/Eto/Drawing assemblies. Machine-readable schema was parsed; semantic validation is exercised in C#, rather than introducing an extra runtime schema engine.

## Recommended next milestone

Complete the supported-host capture and **UX acceptance** milestone first: native panel installation, first-run connection, clear view/resolution readouts, source/result comparison, cancellation/reconnection and save behavior. Exercise authored architectural fixtures on Apple Silicon and Windows, inspect hidden/finite clipping and material cases, verify no document mutations, and measure capture pauses. Fix friction and metadata gaps before adding more providers. The [host checklist](plugin-smoke-test.md) defines observable pass/fail evidence.

Then add **one real reference-image editing adapter**, with explicit cloud opt-in, OS-backed credentials, payload disclosure, supported-size negotiation and an architectural fidelity benchmark. Keep openings, stairs, section voids, proportions and border landmarks as measured criteria separate from style preference.

## Recommended first real image integration

Use the **OpenAI Image API edits endpoint**, initially evaluating the dated **`gpt-image-2.5-sunburst-2026-09-08`** snapshot. Current official documentation positions Sunburst for editing precision and supports direct reference-image edits. That makes it a practical first candidate for this input/output architecture without installing a local ML runtime. This is a recommendation to benchmark, not evidence it preserves architectural geometry better than alternatives. [Image generation guide](https://developers.openai.com/api/docs/guides/image-generation), [edits endpoint](https://developers.openai.com/api/reference/resources/images/methods/edit).

Confirm account availability and current limits during implementation; use one source image and one output initially. Reject unsupported plans instead of silently reframing. Keep source/generated comparison and record geometric deviations. If its benchmark is inadequate, the next useful comparison is a controllable local/office pipeline using aligned structural passes, after those passes themselves are validated. Do not implement every cloud family in parallel.

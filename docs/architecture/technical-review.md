# Phase 0 technical review

Reviewed: 2026-09-19. Status: historical Phase 0 review, subsequently approved. See the [implementation review](../development/phase-4-review.md) for current status. The original minimum-service-release proposal below and the initial delivered 8.21 baseline are superseded by [ADR 0010](../adr/0010-rhino-8-series-compatibility.md), targeting the Rhino 8 series. At the time of this review, evidence was official documentation and read-only inspection of the installed Rhino SDK documentation. No Rhino capture experiment, paid provider request or production implementation had been performed.

## Recommendation

Proceed with a standard RhinoCommon plug-in, native Eto panel, immutable capture snapshot, versioned JSON/PNG Scene Package, and capability-driven adapters. Prove a reference-only round trip to a standalone local development server before adding inference. Build only the passes an adapter can use. Treat structural accuracy as an acceptance criterion requiring measurement and visual review, never a promise created by a prompt.

The core product idea is coherent. Its hardest problem is preserving architectural information through a probabilistic image transformation. A larger rendering-engine SDK or more metadata alone does not solve that problem.

Geometry preservation is a central engineering objective. Provider choice, conditioning passes, prompts and evaluation should actively pursue unchanged architecture and section geometry. Lack of a deterministic guarantee does not make deviations acceptable as successful representation. Evaluate and reduce them explicitly, alongside stylistic quality. The reference-only first slice proves the capture foundation; subsequent generative milestones must demonstrate progress toward this objective.

## Validated assumptions and corrections

| Assumption | Finding | Design consequence |
| --- | --- | --- |
| Native integration requires a render engine | Rhino supports commands and Eto panels independently of `RenderPlugIn`; the latter participates as the selected Rhino renderer. | Standard plug-in first. Do not replace the user's renderer. |
| Rhino 8 has one .NET baseline | Runtime support varies by service release. The documented Rhino 8 recommendation is .NET 8, default from 8.20. | Propose Rhino 8.20+ with .NET 8 selected; no .NET Framework fallback target. |
| Camera location/target/lens completely describe a view | Parallel, two-point perspective, asymmetric frusta, viewport dimensions, and Named View restore behavior matter. | Freeze full projection/frustum and frame, not just a camera name or focal length. |
| Auxiliary passes are easy variations of a screenshot | Visibility, clipping participation, antialiasing, transparency, renderer effects, and display conduits complicate alignment. | Each generator earns availability through fixtures on both platforms. |
| Rhino has no depth extraction | `ZBufferCapture` exists; its minimally documented numeric and sizing behavior needs experiments. | Investigate depth early; do not advertise metric or aligned depth yet. |
| Clipping planes alone identify all cut pixels | Cut fills, holes, occlusion, participation lists, finite depth, and intersecting planes must be resolved. `GetFillSurfaces` provides a useful starting point. | Preserve clipping metadata now; defer section-mask availability until validated. |
| A material has one layer and one universal PBR description | Materials can be reused across layers and object/subobject/instance assignments; custom shaders may only offer an approximation. | Separate material records from bindings; preserve provenance and unknown values. |
| Several reference images imply explicit geometric controls | Image-reference inputs are not automatically depth, normals, or segmentation inputs. | Declare exactly how each input is consumed. |
| Cloud APIs have a common fixed image size | Current APIs vary by model and include constrained custom sizes and resolution tiers. | Negotiate from model-specific limits; validate actual returned pixels. |
| “Preserve exactly” or a fidelity slider guarantees geometry | Image models can move openings or close cut-away regions despite instructions. | No numeric fidelity slider or strict-geometry badge in the initial UI. |

Rhino facts and source links are in the [feasibility report](../development/rhino-feasibility.md). API evidence and unresolved provider details are in the [provider review](../providers/current-capabilities.md).

## Proposed deviations from the brief

1. **Require Rhino 8.20+ initially**, rather than implying every Rhino 8 release is supported. The installed Rhino 8.16 is below this proposed default baseline.
2. **Use ASP.NET Core for the reference server**, rather than adding Python/FastAPI for a server that only validates and returns PNG bytes. Python remains a valid independent inference-server implementation later.
3. **Keep Scene Builder inside the plug-in project initially.** It is a clear internal boundary, but does not need its own assembly before a second consumer exists.
4. **Ship reference capture first.** Other pass names are supported by the draft format; generating material/semantic/object IDs, depth, normals, and section masks is deferred and explicitly unavailable.
5. **Remove the primary fidelity slider.** Preserve geometry as the objective across all modes, supported by prompts and any useful validated guidance passes. Introduce a control only after provider behavior and meaning can be explained honestly.
6. **Show engine/model and destination as a compact status row**, with Change, instead of forcing a new selection on every render.
7. **Use a cancellation action rather than deleting a job to cancel it.** Cancellation, historical records, output deletion, and client disconnection have different meanings.
8. **Delay batch capture, office-scale scheduling, automatic endpoint discovery, reusable office mappings, and cache invalidation.** Keep future batch jobs tied to Rhino Named Views without prematurely implementing them.

These changes preserve the product principles; they reduce implementation and interaction costs.

## Cross-platform and environment findings

Read-only checks found an arm64 Mac, Rhino **8.16.25042.13002**, .NET SDK **10.0.201**, and standalone .NET/ASP.NET runtimes **10.0.5**. Rhino also contains bundled .NET 7 runtime files; the active Rhino runtime was not queried. An installed SDK does not establish which runtime Rhino loads. No applications or runtimes were changed.

Microsoft's .NET 8 support ends **2026-11-10**, so this is a material release-planning constraint. Propose `net8.0` for the Rhino 8 plug-in/shared libraries and `net10.0` for the independent reference server. Before distribution, recheck McNeel's supported runtime and servicing position; do not load a .NET 10-only assembly into a .NET 8 host. [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy), [McNeel runtime guidance](https://developer.rhino3d.com/guides/rhinocommon/moving-to-dotnet-core/).

Cross-platform capture remains a release gate. Rhino APIs expose bitmap types, but standalone `System.Drawing.Common` is Windows-only in current .NET. Confine Rhino-owned bitmap handling to the Rhino host and test PNG encoding there. Core and the reference service should initially move encoded bytes without depending on GDI+. [Microsoft compatibility note](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/system-drawing).

## UX review

Normal use needs a view readout, representation mode, optional prompt, output size, visible destination, and Render. Rhino supplies the rest. Result review needs source/generated comparison, actual dimensions, save/export, and clear status. A result belongs to the captured scene even after the user navigates elsewhere.

Avoid an empty panel full of disabled future providers. Settings can list planned providers as unavailable, with a reason. A developer passthrough engine must say that it returns the source image unchanged and disable style controls. It must never appear to demonstrate a successful competition sketch.

The [five UX documents](../ux/information-architecture.md) define journeys, hierarchy, wireframes, primary-control justifications, and native interaction constraints before implementation.

## Privacy, security, and dependency concerns

Confidentiality includes raster content, layer/object names, prompts, local paths, and thumbnails. Excluding the `.3dm` alone is insufficient. Apply an outbound allowlist after capability negotiation; cloud image adapters should generally receive the reference image and only the necessary approved text. Do not upload unused passes or texture files.

The endpoint is a trust boundary even on an office LAN. Permit HTTP only on loopback by default; use authenticated HTTPS for office servers. An office server's advertised “local” flag cannot prove it will not forward data to a cloud. Office deployment needs an operator-controlled egress policy. Preserve local-only restrictions at provider selection, submission, redirects, and output retrieval.

Store credentials in OS storage through a narrow abstraction, with session-only fallback when persistence is unavailable. Never fall back to plaintext. Use bounded streaming, generated filenames, checksum validation, PNG dimension limits, and ownership checks for jobs. Keep prompts and images out of ordinary logs. Details: [ADR 0005](../adr/0005-authentication-privacy-storage.md).

No dependencies have been installed. Runtime and SDK terms, Eto, test packages, native image codecs, model weights, and inference applications require distinct inventories. An API integration does not grant redistribution rights to model weights. [Dependency inventory](../legal/third-party-dependencies.md).

## Risks and decisive experiments

| Priority | Risk | Evidence needed before claiming support |
| --- | --- | --- |
| P0 | Camera/framing drift during capture | Fixed scene landmarks at all four borders, perspective/parallel/two-point, odd dimensions and display scaling; verify on Windows and Apple Silicon. |
| P0 | Document changes between metadata and passes | Change detection or single UI-thread capture transaction; abort inconsistent snapshots; no live Rhino objects in background work. |
| P0 | Section openings invented by a model | Evaluate real section fixtures against source, including multiple planes and glazing; withhold any strict guarantee. |
| P0 | Secrets or confidential metadata leaked | Outbound payload assertions, redirect tests, log inspection, OS-store failure tests, endpoint allowlist tests. |
| P1 | Incorrect material assignment | Object/layer/default/backface/subobject/nested-block fixtures; unknown values survive serialization. |
| P1 | Depth/ID maps misaligned | Shared projection and raster size; occlusion and clipping fixtures; no blended ID colors or hidden geometry leakage. |
| P1 | Cancellation or retry causes duplicate billing | Lost-submit-response and cancellation/completion races; idempotency for the local protocol; no blind cloud POST retries. |
| P1 | Large captures stall Rhino or exhaust memory | Measure UI capture duration, peak decoded buffers, encoded output, disposal, queue limits, disk-full failure. |
| P1 | Host runtime approaches end of support | Reassess supported Rhino 8 updates before release; do not infer Rhino 9 compatibility. |

## First provider recommendation

Proposed first backend to implement: **VisoLoom Reference**, an explicitly non-generative local passthrough service. This isolates Rhino/protocol defects from model behavior.

First real integration to evaluate: **OpenAI Image API edits with a pinned, available GPT Image model**, initially one reference and one result. The current editing and dimension controls make it a practical baseline without GPU installation. This recommendation is about integration scope, not proven architectural accuracy or a permanent default. Use a small non-confidential architectural benchmark before accepting it. If cloud use is disallowed, the next milestone instead becomes one separately licensed local image-editing pipeline behind the same protocol. [Official image guide](https://developers.openai.com/api/docs/guides/image-generation).

BFL is a strong subsequent candidate with explicit asynchronous jobs; Gemini deserves comparison for its higher-resolution options. xAI remains a researched candidate. None has been integrated or benchmarked.

## Decisions raised in Phase 0

The architecture and reference-only first slice, including the proposed Rhino 8.20+ baseline and clearly labelled developer backend, were approved and implemented; no further phase approval is pending. The initial delivered stable-package baseline of 8.21+, recorded in [ADR 0009](../adr/0009-first-slice-delivery-boundaries.md), was later replaced by the clarified Rhino 8 series target in [ADR 0010](../adr/0010-rhino-8-series-compatibility.md). Repository ownership is tied to the authenticated GitHub account. The repository is public as `PascalNun/VisoLoom` and licensed GPL-3.0-or-later.

Still unresolved by documentation: target-host capture/PNG behavior, high-resolution depth alignment, reliable ID and section-mask extraction, runtime servicing beyond November 2026, exact credential-store packaging/signing, and first-provider benchmark quality. These are scheduled experiments, not silently assumed capabilities.

## Phase 0 verification

The review contains 28 Markdown documents and eight proposed ADRs. A local check verified all 55 relative document links, Markdown fence pairing, required UX files, ADR proposal status, and the arithmetic of the padding/resolution examples. No `LICENSE` or Git repository was created. These checks validate the documentation package, not Rhino behavior, HTTP implementation, model quality, or production security. Those remain explicit post-approval tests.

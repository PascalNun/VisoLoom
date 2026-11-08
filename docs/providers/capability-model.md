# Provider capability and adapter proposal

Status: accepted adapter contract `1.0`. The local passthrough and one pinned OpenAI image-editing adapter are implemented; Google and xAI adapter scaffolds are implemented and fail closed until their size contracts are pinned (ADR 0020); BFL and local FLUX remain unavailable. OpenAI transport tests pass; real generation and native codec acceptance remain pending. Neutral capability/result records are in `VisoLoom.Core`, HTTP messages are in `VisoLoom.Protocol`, and the execution interface is `IRenderProvider`.

The table below defines the wider integration target. The first slice implements reference-image input, prompt/cancellation/padding/resampling declarations and size constraints. Per-pass consumption strategies, seeds, costs, batch limits and enterprise policy need concrete adapter work before they are available. See the [implementation review](../development/phase-4-review.md).

## Capabilities describe tested behavior

A provider family is not a model capability. Each descriptor identifies provider, endpoint, model ID/version, adapter version, evidence date, availability (`ready`, `needsConfiguration`, `notImplemented`, `unavailable`), and diagnostic reason. Discovery can update a catalogue but must not select a different model or grant new privacy permissions.

| Group | Required distinctions |
| --- | --- |
| Inputs | Text, reference image, edit workflow, maximum reference count, per-image and aggregate bytes/pixels/MIME. |
| Per-pass usage | `nativeControl`, `editMask`, `visualReference`, `textContext`, or `unused`, with encoding, count, and combination restrictions. |
| Output | Size rules, max/min pixel count, edge multiples, aspect limits, fixed native sizes or tier table, formats, alpha support. |
| Intent | Presets applicable to this model; optional guidance controls and their actual provider meaning. No invented common fidelity scale. |
| Execution | Progress `none`/`stage`/`percentage`; cancellation `none`/`clientOnly`/`bestEffortRemote`/`confirmedRemote`; seed acceptance separately from repeatability; batch/result count limits. |
| Trust | Configured endpoint origin, local/office/cloud destination, allowed metadata policy, supported authentication schemes. Provider claims cannot override enterprise policy. |
| Cost | Optional currency, unit/rate source, timestamp, estimate range and uncertainty. Null means unavailable. |

Capability support needs both API documentation and adapter implementation tests. Record provider-native limits without converting marketing “4K” into guessed pixel dimensions. An unavailable field must be explicit and fail closed when a request requires it.

## Adapter boundary

Proposed operations, in language-neutral terms:

- `DiscoverAsync(endpoint, credentialReference, cancellation)` → catalogue and capabilities.
- `RenderAsync(immutablePackage, renderPlan, authorizedCredential, progress, cancellation)` → validated neutral result or typed error.

The adapter keeps provider job IDs and transport details internally while reporting them into optional provenance. Its asynchronous method may poll a server or await one HTTP response. This avoids forcing synchronous cloud APIs to invent native server jobs. Execution is not restarted by re-opening the panel.

Core prepares the plan; the adapter checks it against the captured capability revision before submission, composes provider-native parameters, and reports consumed and omitted information. Raw provider JSON and secrets never reach UI models. No dynamically downloaded code or arbitrary executable workflows in capabilities.

The local adapter and server implement only `reference -> passthrough`. The OpenAI adapter implements one direct reference edit with quoted text context; its native/live acceptance remains pending. The Google and xAI scaffolds compose their documented requests and parse responses, but rendering fails closed with `sizeContractUnpinned` until endpoint research pins verified pixel tables; the xAI scaffold also refuses hosted result URLs until that download contract is researched. BFL descriptors remain `notImplemented`, with a failure before network or credential access if invoked. Do not return sample images as if those providers rendered them.

## Preflight policy

Intersect user intent, preset requirements, Rhino pass-generator availability, provider capabilities, and enterprise policy. Required features must survive the intersection. Preferred passes may be omitted with a recorded explanation, but explicit requested material/section behavior cannot quietly disappear. A prompt-only preservation instruction is classified as intent, not a structured control.

Geometry preservation remains a core objective regardless of that input classification. Evaluate each provider strategy against representative architectural captures, record observed geometric deviations, and use the evidence to select useful guidance controls and assess readiness. Limited provider controls constrain the available techniques; they do not make preservation optional.

Do not send a material-ID image as an extra picture unless a tested adapter strategy explains its use; a provider could paint the false colors into the result. Explicit geometry-control backends should declare preprocessing and supported encodings, including depth normalization and mask polarity.

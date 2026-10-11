# VisoLoom implementation review

Reviewed 2026-10-11. Phases are delivery checkpoints, not additional approval gates. Approved Phase 0 decisions remain authoritative, with the user's later compatibility, UX and first-provider refinements. [Repository](https://github.com/PascalNun/VisoLoom): private; no project LICENSE, public release or Yak publication.

## Working functionality

- Buildable solution with Rhino plug-in, independent Core, provider boundary, versioned local Protocol and optional reference service. Dependencies/lockfiles remain pinned; this integration adds no dependency.
- Immutable Scene Package with camera/frustum/frame invariants, eligible layer/material context, clipping participation, schema/semantic validation and hashed reference PNG. Capture does not intentionally write Rhino's model or view.
- Authenticated local round trip, persistent bounded jobs, idempotency/reconnect, cancellation, retention and byte-identical reference return. The launcher connects automatically through a same-OS-user handoff without token paste.
- Native panel and commands, complete-frame resolution choices, source/result previews and atomic PNG export. User screenshots confirm Mac Wireframe and standard Rendered perspective captures at 2358 × 1810, automatic connection, corrected layout at the shown width and successful ordinary saving.
- First OpenAI direct image-editing adapter: exact pinned model discovery, one PNG input/output, bounded context, response validation, provenance, fixed destination and no automatic generation retries. Seven presets, optional prompt and prepared-prompt inspection are wired into the panel.
- Connection settings registered in Rhino Options/Preferences with a panel shortcut; explicit cloud opt-in, optional names and optional Keychain/Credential Manager storage. Model selection preserves intent and sends nothing. Nonsecret preferences persist per user; document model/preset/prompt selections are currently session state.
- Original brand sources organized under `assets/brand/source`, the supplied board under `reference`, and concise brand documentation. SVG/VectorCraft/board bytes are preserved; existing runtime icon/resource references are unchanged. Precise / Structured with symbol right is primary; final typography, production gradients and icon exports remain open.
- Numerical resolution planning plus implemented host image processing for complete-frame fitting, padding and normalization. Both native and normalized cloud outputs are retained privately with hashes. The Mac padding/normalization path has first real-render evidence; resampling remains unverified.

**Evidence boundary:** local capture has native Mac evidence. New user screenshots confirm Settings, remembered OS key storage and access to the pinned model. The first cloud render captured the view but failed in reference preparation: Rhino's Mac drawing assembly lacks `ImageAttributes.SetWrapMode`. An isolated probe reproduced the failure before any image POST. That call is removed, and the corrected codec compiles directly against the installed Mac assembly. The user subsequently completed a real OpenAI edit and PNG export. Local provenance and file hashes confirm capture 2358 × 1810 → native output 2368 × 1824 → export 2358 × 1810, scale 1, with only the added 5-pixel side and 7-pixel top/bottom border removed. The exported PNG matches the locally recorded generated result exactly. This is one successful Mac padding path, not a systematic architectural-fidelity result or resampling test.

## Stubs and deferred work

BFL, Google, xAI, local FLUX and office inference remain deferred. Auxiliary edge/depth/normal/material/semantic/section passes, localized correction, region locks, full lighting/PBR extraction, personal preset editing/saving, multiple saved connections and reopened history browsing are not implemented.

The asset browser, asset production and material collection remain planned. Their geometry/materials must already communicate the intended appearance in Rhino; placement, scale and variation stay with the designer. No third-party assets or geometry-generation runtime were added.

## Changed assumptions

- Target the Rhino 8 series using its default .NET runtime; compile against original stable RhinoCommon 8.0.23304.9001 with net7.0 shared/plugin assemblies. This removes the earlier later-service-release requirement without claiming every host has passed native tests. Installed Rhino 8.16 remains unchanged. [ADR 0010](../adr/0010-rhino-8-series-compatibility.md).
- Automatic local token handoff replaces manual first-run setup while keeping HTTP authentication and explicit manual overrides. [ADR 0011](../adr/0011-automatic-local-development-connection.md).
- The user's chosen OpenAI-first milestone enables one cloud editing adapter alongside the unchanged reference test. Geometry preservation remains the objective in every preset; a successful API response does not establish fidelity. [ADR 0012](../adr/0012-first-openai-image-edit.md).
- Supported model views remain perspective/parallel Wireframe, Shaded and standard Rendered. Custom modes, two-point perspective and layouts/details are rejected pending validation.
- Account-based OpenAI sign-in is now documented, but its current plan-usage flow explicitly excludes image generation. API-key billing remains necessary for this adapter; revisit supported OAuth later without copying consumer sessions.

## Known limitations

The native host matrix is incomplete, particularly Windows, narrow panels, high DPI, graphics/display modes, clipping and complex materials. Camera/reference preservation is an input invariant; model drift in openings, stairs, sections or proportions is a defect to measure and reduce. No architectural fidelity benchmark has run.

Scene context is bounded and approximate. It does not map materials to pixels or include full render-content, nested/subobject assignments, texture mapping or lighting provenance. Custom AO/edge capture is still gated even though it is a product priority. Metadata does not compensate for an inaccurate source image.

OpenAI Stop waiting ends local waiting only; remote completion and charges can remain unconfirmed. There is no cloud reconnect/resubmission recovery. Corporate proxy support is not implemented. The model/caps are pinned from current documentation; real account access, latency and cost still need a test.

Private history contains full captured scene context; Windows profile ACL inheritance needs deployment validation. Same-user Keychain access is not a substitute for testing packaged Rhino signing/access prompts. Slow capture, document-close, disk-full and native save overwrite/cancel/error behavior remain acceptance cases. A result-history write failure can prevent display after successful generation.

## Blockers

No build or automated-test blocker remains. A key is now configured by the user, with successful model discovery and remembered storage. The first generation and export have now completed after the Mac image-preparation fix. Keys stay in Settings/OS storage, never chat or Git. Save/restart Rhino and rerun the launcher to load changed code; an already loaded plug-in cannot be replaced in place. The existing user service was left running.

Native resampling and Windows credential tests remain release gates. Settings text wrapping also needs refinement at the shown dialog width. McNeel SDK/distribution terms still need confirmation before distributing binaries; VisoLoom licensing remains undecided.

## Test status

**67 local tests pass: 24 Core and 43 protocol/provider tests; zero build warnings/errors.** Added regressions assert zero HTTP submissions after a host preparation failure and exactly one submission, with a possible-charge message, after a normalization failure. Safe errors omit raw exception details. Tests cover frame planning, version/JSON validation, prompt preservation, local authentication/Host/Origin checks, upload/queue limits, cancellation/restart/expiry, credential handoff and idempotent recovery. New simulated OpenAI checks cover exact prompt/image payload, optional-name disclosure, pinned discovery, malformed responses, rejection, source tampering, output dimensions, padding-plan handoff and no billable retry. No confidential fixture or paid request was used.

The prior [43-test automatic-connection CI](https://github.com/PascalNun/VisoLoom/actions/runs/38167463771) passed macOS and Windows. The initial integration CI passed Mac but exposed an intermittent Windows local-handoff restart failure. The listener now binds before startup readiness and waits for client closure before disconnecting, preserving the existing deadline and authentication. Repeated handoffs are asserted. The [final integration CI](https://github.com/PascalNun/VisoLoom/actions/runs/38173755711) passed Mac and Windows at source commit `1510e5e`: all 64 tests, build and plug-in deployment/resource gates passed. Subsequent documentation-only changes do not alter that source. Prior isolated probes under Rhino's actual .NET 7 confirmed local handoff/HTTP behavior; a new synthetic probe also passed Keychain write/read/update/delete and mocked OpenAI discovery under that runtime. All seven embedded plug-in presets also loaded and validated under that runtime. This is runtime/library evidence; subsequent user screenshots add native Mac Settings/model-access/key-storage evidence. The Mac codec failure has been reproduced in an isolated probe, and its fix compiles against the installed host drawing library. An attempted in-Rhino synthetic probe could not run because UI automation timed out; subsequent user evidence confirms the live Mac padding and generation path; resampling and wider host acceptance remain pending.

## Recommended next milestone

Build on the first completed OpenAI edit from the Settings/panel flow. Keep further benchmark fixtures non-confidential. Validate returned size, PNG saving, exact recorded context, source comparison and stopping behavior. Exercise odd-size padding and a labelled border fixture in Rhino before broadening the provider list. Then benchmark openings, stairs, section voids, proportions, border landmarks, materials and planting separately from style preference, latency and cost. Complete the [host acceptance matrix](plugin-smoke-test.md), including the user's custom AO/edge style and Windows UX.

## Recommended first real image-model integration

The selected first adapter is OpenAI direct edits with `gpt-image-2.5-sunburst-2026-09-08`. Its documented reference-editing workflow and editing focus fit the architecture without installing a local ML runtime. This justifies a first benchmark, not a claim of superior geometric fidelity. [Image guide](https://developers.openai.com/api/docs/guides/image-generation), [edit contract](https://developers.openai.com/api/reference/resources/images/methods/edit).

If it fails the architectural benchmark, evaluate a controllable local/office pipeline with validated aligned structural passes. Implementing every cloud family now would not resolve input fidelity or UX gaps.

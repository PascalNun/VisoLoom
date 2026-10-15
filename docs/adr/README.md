# Architecture decision records

Records 0001–0008 were **accepted by the user on 2026-09-19** to authorize Phases 1–3. They intentionally refine the original brief. Implementation evidence and remaining validation gates are tracked separately.

| ADR | Decision |
| --- | --- |
| [0001](0001-rhino-host-and-runtime.md) | Standard Rhino plug-in and explicit host/runtime baseline |
| [0002](0002-scene-snapshot-and-package.md) | Immutable scene snapshot, JSON/PNG package and external storage |
| [0003](0003-http-protocol-reference-server.md) | HTTP jobs and a small independent reference server |
| [0004](0004-capabilities-and-resolution.md) | Model-specific capabilities and one resolution planner |
| [0005](0005-authentication-privacy-storage.md) | OS credentials, explicit network policy and minimal disclosure |
| [0006](0006-rhino-threading-and-jobs.md) | Host-thread capture and asynchronous detached jobs |
| [0007](0007-native-ui-and-fidelity.md) | Native Eto UI and honest preservation intent |
| [0008](0008-dependency-boundaries.md) | Small dependencies and optional inference runtimes |

[0009 — First-slice delivery boundaries](0009-first-slice-delivery-boundaries.md) records implementation refinements and explicit remaining gates within the authorized scope.

[0010 — Rhino 8 series compatibility](0010-rhino-8-series-compatibility.md) implements the user's clarified target on 2026-10-11, superseding the earlier minimum-service-release and runtime choices.

[0011 — Automatic local development connection](0011-automatic-local-development-connection.md) implements the requested first-run UX without removing HTTP authentication or persisting temporary credentials.

[0012 — First OpenAI reference-image edit](0012-first-openai-image-edit.md) records the selected first adapter, native Settings, minimal disclosure, OS credentials and remaining live/host validation.

[0013 — Named Views and capture controls](0013-named-view-capture-controls.md) implements the user's saved-composition priority, explicit visible view restoration, linked exact-aspect sizes and regular custom-style capture with native evidence.

[0014 — Two-point perspective capture](0014-two-point-perspective-capture.md) records native asymmetric-frustum evidence, accurate projection metadata and numerical tolerance for saved-view matching.

[0015 — Preview before submission](0015-preview-before-submission.md) freezes the locally reviewed image and prompt, rejects stale previews and submits without recapture.

[0016 — Panel hierarchy and Named View browsing](0016-panel-and-named-view-browser.md) groups the workflow and adds local composition thumbnails without an independent camera manager.

[0017 — Local engines and model-specific prompts](0017-local-inference-and-model-prompts.md) separates managed local setup from self-hosted connections and begins a concise-prompt FLUX 4B calibration with the existing Draw Things installation.

[0018 — Prepared local models and framing](0018-prepared-local-model-and-framing.md) adds reusable verified weights, the authenticated engine bridge, concise prompts and recorded uniform sampling for small local canvases.

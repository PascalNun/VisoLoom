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

[0019 — Completed-image inspection and saving](0019-result-inspection-and-saving.md) records one render workflow, large source/result toggling, durable automatic PNGs and an optional additional output folder.

[0020 — Google and xAI adapter scaffolds](0020-google-xai-adapter-scaffolds.md) implements the first two comparison adapters fail-closed: request composition and response handling exist, rendering waits on pinned size research and the xAI URL-result policy.

[0021 — BFL adapter scaffold](0021-bfl-adapter-scaffold.md) completes the researched provider set with a fail-closed FLUX.2 pro adapter: static pinned discovery, and rendering gated on both unpinned size limits and the unresolved reference-upload contract.
[0022 — Bundled engine infrastructure and one-click model installation](0022-engine-bundling-and-one-click-model-install.md) decides the packaging direction for local inference, records the non-Apple spike outcome (a full 512 x 448 klein Q4_K reference edit through the ggml stack at 98 minutes on a 4-core CPU; Vulkan dies with device lost on Kaby-Lake-class integrated graphics) and keeps the end-user install free of third-party platform accounts.

[0023 — Editable dimensions and complete-frame export](0023-linked-export-dimensions.md) separates linked export dimensions from exact-aspect Rhino capture and model grids, with bounded enlargement and aligned comparison.

[0024 — Account-listed OpenAI model selection](0024-account-listed-openai-model-selection.md) makes the authenticated account list the selection source: every image model it offers is selectable on the family baseline contract, the compiled profile marks the tested snapshot, and forward compatibility replaces per-model pre-verification.

[0025 — Google account-listed model selection](0025-account-listed-google-model-selection.md) extends the same pattern to Google's Interactions surface: account-listed family models, no invented aspect or size values, documented input planning and normalization back to the Rhino frame.

[0026 — xAI account-listed model selection](0026-xai-account-listed-model-selection.md) wires the third cloud the same way: the account's Grok Imagine models, the documented 2k tier attempted first with one verified fallback, inline results decoded in their documented mime (JPEG by default), hosted links never fetched.

[0027 — BFL wired around its unresolved reference contract](0027-bfl-wired-around-reference-gate.md) wires the last cloud: a documented credits check validates the key, the curated pro pair is selectable, and rendering stays closed on the owner's open reference-upload decision — no image is sent and no host is contacted.

[0028 — Settings connections view](0028-settings-connections-view.md) replaces the four stacked provider forms with one saved-connections list: every destination visible with its state and reason, per-provider details with supported models, a shared data-described check flow, and provider-correct handoff from the Models tab.

[0029 — Personal preset editor and persistent representation defaults](0029-personal-preset-editor.md) implements the accepted preset paragraph: Edit preset…, Save as new preset…, named personal presets in a fail-closed application-data store with store-stamped revisions, deletion behind an explicit confirmation, and the last chosen preset restored as this user's default.
[0030 — Safe-frame framing inside the locked view](0030-safe-frame-framing.md) decides the framing plan's open questions: a screen-space conduit shows the centered lock-off frame and disables itself around the capture, the validated viewport capture is followed by frame-rectangle extraction into the record, and ADR 0023's rejection of unrelated aspects becomes accepted-with-frame inside the existing limits.
[0031 — Consent on connect, keys remembered by default](0031-consent-on-connect.md) records the owner decision: an API key plus connect is the consent to render — no separate allow-rendering control — and keys live in the OS store until explicitly forgotten, with the transmit-fields disclosure kept.

[0032 — Gated weights download with the user's Hugging Face token](0032-gated-weights-huggingface-token.md) decides the owner's question about gated catalogue rows: acceptance stays on the publisher's page and never moves into the app, while a user-entered Hugging Face access token — stored beside the provider keys — lets licensed weights download in-app as the user, verified against the same pinned sizes and hashes.

[0033 — Downloads run with the Rhino session](0033-session-scoped-model-downloads.md) moves the in-flight download out of the Settings window: one session-scoped download keeps running after Settings closes, Settings re-attaches on open, and every stop path leaves no staged bytes behind.

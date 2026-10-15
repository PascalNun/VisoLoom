# User journeys

Status: accepted interaction target, refined by the product interview on 2026-10-15. OpenAI generation/export have user-tested Mac evidence. The prepared local engine has worker/service evidence; its latest loaded Rhino workflow remains pending. Personal presets, additional providers and broader journeys remain targets. See the [implementation review](../development/phase-4-review.md) for evidence and limitations.

## First useful representation

Prerequisite: one allowed real engine is configured. The user sets the view in Rhino, opens VisoLoom, chooses Preset, optionally adds a prompt, chooses Resolution, and prepares a local capture preview. After reviewing the image and intent, Render submits that frozen capture. Defaults keep optional adjustments out of the way. No additional camera, material import, scene wizard, or pass selection is required.

Infer camera/projection/frame, units, current display mode, eligible visible materials/layers, active clipping and applicable preset context. Show view and destination readouts so the inference is reviewable. If the active view differs from a restored Named View, identify it as current/modified when reliably known; never pretend the saved view was used.

After capture, the user can continue modelling. The completed result appears in the panel and is saved automatically as a durable PNG. Open the large comparison viewer to toggle between the result and its original Rhino capture at the same zoom and position. Save PNG… remains available for a deliberate export. View and timestamps refer to the capture, not the current document state.

## Reusing a prompt preset

The designer chooses **Photorealistic** or **Competition Sketch** from Preset, optionally adds a project-specific note, then renders with the configured model. For a recurring house style, open **Edit preset…**, adjust the representation instructions and **Save as new preset…**, for example “Studio competition sketch.” The named version is available next time without retyping the instructions. Changing Preset keeps the current optional note; choosing or editing a preset alone submits nothing.

Every preset aims to preserve the Rhino design. A visual treatment such as Modern Illustration affects the drawing, not the building's architectural style. Before submission, the chosen model must support the preset's required behavior. The captured job records the actual instructions; subsequent preset edits affect future renders. This journey is planned; the existing sample preset data and prompt composer do not yet provide a personal-preset editor.

## First-time setup

If no engine is configured, the panel shows the current view and a single Set up engine action. For local setup, begin with a small model catalogue and guidance on which choices suit the user's computer. This catalogue is for setup; it must not present uninstalled or unsupported models as ready rendering choices. Existing compatible folders and separately configured server connections remain additional paths. The user sees endpoint/provider, authentication requirement, destination and payload summary before saving a connection. Credentials persist only in supported OS storage; otherwise offer session-only use. Catalogue/setup implementation still follows runtime, model-source, distribution and hardware gates; opening it downloads nothing.

Confirmed catalogue presentation: a few curated choices, with plain-language strengths, hardware fit, download size and local/cloud location. Do not lead with a long technical model inventory. The existing folder inspector remains useful, but is not the complete first-use catalogue or an automatic installer.

The first development milestone has a `VisoLoom Reference` engine, described as **Returns the Rhino capture unchanged**. Preset and Prompt are unavailable and hidden with that explanation; the action is **Test capture**. Starting **Test VisoLoom** connects the panel automatically through a same-OS-user local handoff, including fresh tokens after a service restart. Connection checks send no scene. **Settings → Manual local connection…** is an explicit manual override, not a required first-run step. This developer exception avoids presenting passthrough as a generated representation. The normal product action remains Render once a generative engine exists.

## Switching models during everyday work

After setting up connections once, the designer chooses a ready model from **Model** in the main panel, for example a configured OpenAI editing model or a local FLUX model served by a compatible local engine. OpenAI and the local capture test are implemented choices; prepared local FLUX selection is implemented experimentally and awaits loaded Rhino acceptance. No settings dialog is needed to switch between already configured, allowed choices. The destination readout updates immediately; nothing is sent until Render.

Keep the existing prompt, mode and resolution. If the new model cannot satisfy them, explain the conflict and offer valid adjustments. Unsupported intent blocks submission rather than disappearing. Switching to a cloud destination respects project policy, explicit first-use opt-in and the payload summary. A local-only project never exposes a usable cloud choice.

While a render is running, choosing another model prepares the next render. The running job, comparison and saved provenance retain their original model and capture. Returning to a previous model restores its supported model-specific preferences without overwriting the common prompt. An offline connection offers Check connection or Settings; no automatic provider fallback occurs.

## One render, large inspection and automatic saving — interview decisions

The user's later clarification on 2026-10-15 supersedes the earlier draft/final preference. Keep the same ordinary Render workflow for local and cloud engines. A smaller generation is not a dependable preview of a later generation, so no draft/final stage or implied appearance lock is introduced. Preview capture is a local inspection of the frozen input, not an AI-generated draft; its submission guard stays unchanged.

The preferred large comparison is one image with a quick source/result toggle, with shared zoom and pan. Always pair a result with its stored, unpadded Rhino reference. Moving the active viewport or editing the model must not substitute a new source into that comparison. Any future new generation from a stored capture versus a new live capture needs an explicit action; no camera is restored automatically. Enlargement is not labelled as new model detail.

Every completed PNG gets a durable automatic copy outside bounded scene/prompt history. Settings → Image saving… and the result section offer an additional output folder for future renders. That folder receives only the PNG; changing it does not move previous images or change a running job's saved destination. A failed save keeps the result available and explains the next action. Save PNG… remains the immediate one-off export. Neither inspection nor saving starts generation. Unfinished/damaged history is retained rather than risking silent image loss.

## Section perspective

The architect sets Rhino's clipping planes and activates the section view. VisoLoom detects active clipping and uses the captured reference plus available clipping context. The panel indicates when a section is present. Section treatment appears only if implemented and meaningful for the selected engine.

If an explicitly requested mask-dependent treatment cannot be supported, Render is blocked with the reason and available alternatives. A standard reference-based section request may proceed with a clear statement that no section mask is used and source comparison is required. Do not fill removed geometry or reinterpret a black cut fill as a request for a new façade.

## Material study

The architect keeps Rhino's assigned material and may supply a representational description such as “light washed brick.” The future material-description inspector attaches to Rhino identities; it does not recreate a material editor. A region-specific change requires a tested regional adapter capability. If only whole-image prompt context is available, say so and do not label the change as object-local.

## Long render, cancellation, and recovery

Show the captured view thumbnail, job stage, elapsed time, and genuine progress where supplied. A queued job says queued; an unknown percentage is indeterminate. Cancel before submission stops local work; after submission its label/explanation reflects remote capability. If only waiting can stop, use Stop waiting and explain that generation may continue.

A network failure preserves the job ID and offers Reconnect when possible. Retry never silently changes preset, model, view, or quality. If the failed job can safely be retried from its capture, offer Retry this capture; rendering the newly edited model is a distinct Render current view action.

## Future batch

Choose existing Rhino Named Views and representation/output settings once. Each job must explicitly define when geometry/visibility state is captured; a Named View is not a full scene-state snapshot. Before implementing batch, decide whether all views share one captured document revision or capture current state sequentially. Do not introduce a VisoLoom view collection to hide that unresolved behavior.

## Usability acceptance

Test with an untagged model and a returning user. Observe time to first submission, unnecessary setting changes, confusion over current/captured view, understanding of output size, cancellation expectations, and ability to identify an altered opening. Also observe whether a returning user can switch configured models directly, identify local versus cloud execution, fix a capability conflict without losing the prompt, and distinguish the running job from the next model selection. Confirm that connection checks and selection alone send no scene. These are proposed tests; no usability sessions have been run.

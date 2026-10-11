# Information architecture

Status: accepted interaction target. Companion documents: [principles](product-principles.md), [journeys](user-journeys.md), [wireframes](panel-wireframe.md), [native interaction](rhino-native-principles.md).

## Primary panel

| Element | Why it exists | Default / inferred information |
| --- | --- | --- |
| View readout | Prevents representing the wrong composition. | Active Rhino view and projection; no editable camera fields. |
| Preset | Chooses reusable prompt instructions for the representation. | Last compatible choice, initially Competition Sketch for an implemented generative engine; editable and reusable. |
| Prompt | Adds project-specific intent that cannot be inferred safely. | Empty, optional multiline field; no chat transcript. |
| Resolution | Controls a consequential output requirement. | Viewport pixels; 2×, 4× and aspect-locked Custom. |
| Model selector + destination | Makes the execution target, cost and privacy visible; supports quick switching. | Last allowed configured model; native dropdown with destination readout. Setup lives in Settings. |
| Render | Submits the visible intent and current Rhino snapshot. | Enabled only after valid preflight. |
| Latest result/job | Supports progress, review, save and recovery. | Shows source capture association, not a new render environment. |

The view is a readout, not another required selection. No fidelity slider: providers do not share an established architectural scale. Every representation mode aims to preserve geometry. Mode changes the representational treatment while source comparison helps identify architectural deviations and communicate model limits.

## Prompt presets

**Product target; the capture-test build does not generate from presets.** Use a native **Preset** dropdown in the main panel, separate from **Model**. Preset chooses the intended representation; Model chooses the engine that produces it. A designer can choose a familiar starting point without writing the same instructions on every render.

Start with **Photorealistic**, **Competition Sketch**, and **Architectural Sketch**, alongside the existing Concept, Section Perspective, Material Study and Custom starting points. **Modern Illustration** is a possible later visual treatment to refine with examples; it must not ask the model to redesign the building in a modern architectural style. Keep the initial list small and understandable. Existing editable sample data lives in [examples/presets](../../examples/presets); its current Realistic entry is the starting point for Photorealistic, not a validated photorealistic result.

Each preset has a readable name, a short description and editable representation instructions. **Edit preset…** exposes those instructions; **Save as new preset…** creates a named personal version while retaining the supplied starting point. Personal presets live in user settings, outside `.3dm` and Git unless deliberately exported or shared. Keep the optional **Prompt** field for this render's additions. Switching presets changes the preset instructions without clearing those additions. No chat transcript or separate prompt workspace is needed.

Compose the chosen preset, optional user prompt, supported scene context and the shared geometry-preservation objective. Prompt editing does not remove that objective. All presets aim to retain architecture, camera composition and section geometry; Photorealistic refines the visible materials and lighting, while Competition Sketch changes their representation. Presets do not silently replace assigned Rhino materials, alter the scene, change model or resolution, or promise deterministic fidelity. Keep the complete composed prompt inspectable in Advanced.

Presets are provider-neutral intent. On a model switch, retain the chosen preset and check its requirements against the adapter; explain unsupported behavior instead of selecting another style. On submission, record the preset identity/revision and actual instructions with the job. Editing or saving a preset later must not change a running job or its provenance. Personal-preset saving and this UI remain future implementation work; sample JSON and prompt composition already exist.

## Contextual information

Section presence, unavailable requested treatment, native/final size differences, estimated cost if reliable, and outbound destination are shown when relevant. Unsupported requested behavior is never hidden inside Advanced. Vegetation interpretation can appear as a discrete architectural choice once its supported behavior is explainable; preserve position, scale and mass in the default sketch instruction.

## Advanced

Collapse source display-mode override, pass availability/usage, effective prompt, explicit native size and processing plan, seed if supported, provider-specific parameters, material/semantic context summary, and sanitized diagnostics. Only expose implemented controls. Do not display raw JSON as the primary configuration experience.

## Settings and history

Settings owns engines, approved endpoints, credentials, privacy policy, local storage and retention. History owns external job/result records and exports. Rhino continues to own material and view editing. Opening settings or changing a future default does not mutate a submitted job.

## Model choice and connection settings

**Interaction target, not implemented provider switching.** The main panel has one native **Model** dropdown listing configured models, grouped by destination: **This computer**, **Office server**, and **Cloud**. A label combines provider and model when needed to distinguish entries. The destination stays visible beside or below the selector, including the approved server or cloud provider. Choosing a model must not itself upload a capture or start generation.

Settings has a **Connections** view. Each saved connection shows a friendly name, provider/server, destination, connection status and its supported models. **Add connection** asks for an implemented provider or supported server, its address where relevant, and credentials. **Check connection** tests authentication and discovers capabilities; it does not submit an image or run a billable generation. Saving a connection and choosing its default model are separate, explicit actions. Show offline or missing-credential entries with a reason; never silently choose another model.

For an OpenAI integration, setup would use the user's API credentials and the adapter's supported image-editing models. For a local FLUX integration, setup would connect to a compatible inference server on this computer and select an installed model that the server advertises. These are examples of future connections, not available engines. VisoLoom does not install an ML runtime inside Rhino or promise compatibility with any arbitrary FLUX server. Model installation and starting that service belong to its documented setup.

After connection setup, switching between ready models takes one selection in the main panel. Keep the user's prompt, mode and requested resolution. Revalidate them against the selected model's capabilities; if an intent is unsupported, explain the specific conflict and show available choices before enabling Render. Do not discard intent, silently crop, reset quality, or replace a chosen mode. Any changed processing and reliable cost estimate appear before submission. A model's extra controls appear in Advanced only when implemented and meaningful for that model.

Project policy filters allowed destinations. A local-only project cannot select a cloud model. A newly configured remote destination requires the existing explicit opt-in and payload disclosure before its first submission; fast switching never bypasses that policy. Approved destinations remain visible without asking the user to repeat unchanged setup on every render.

Changing the selection during a render affects the next submission. The running job and its result keep the original provider, exact model identity/version, captured intent and processing plan. Credentials belong to the OS credential store when supported, otherwise the session; they never belong to `.3dm`, project settings or Git. Saved connection preferences are user settings, with only deliberate project overrides and destination restrictions. A friendly model name must not silently resolve to a different model version for an existing job.

The capture-test build currently exposes only VisoLoom Reference and its session connection dialog. Cloud credentials, a local FLUX adapter, saved multi-connection settings and the Model dropdown remain future implementation work.

## State ownership audit

| State | Authority |
| --- | --- |
| Camera, Named Views, layers, materials, clipping | Rhino document/view |
| Captured state | Immutable Scene Package |
| Mode/prompt/output defaults | VisoLoom user settings or explicit project overrides |
| Active submission settings | Immutable render intent and plan |
| Provider constraints | Versioned model capability descriptor verified by adapter |
| Job lifecycle | Shared document-associated job service; server state reconciled through adapter |
| Credentials | OS credential store or current session |
| Large results and provenance | External application-data storage / explicit export |

Panel instances are projections of these authorities, not additional owners. UI handlers call planning/execution services; they contain no scene traversal or provider request JSON.

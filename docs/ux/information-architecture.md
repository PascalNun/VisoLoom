# Information architecture

Status: accepted interaction target. Companion documents: [principles](product-principles.md), [journeys](user-journeys.md), [wireframes](panel-wireframe.md), [native interaction](rhino-native-principles.md).

## Primary panel

| Element | Why it exists | Default / inferred information |
| --- | --- | --- |
| View | Identifies the Rhino composition to represent. | Current view or an existing Rhino Named View, with its projection and display style. |
| Representation | Chooses reusable style instructions. | Last compatible choice, initially Competition Sketch for an implemented generative engine. |
| Prompt | Adds project-specific intent that cannot be inferred safely. | Empty, optional multiline field; no chat transcript. |
| Output size | Controls the saved image dimensions. | Viewport, 2×, 4× and linked Custom width and height. The size plan keeps the complete Rhino frame. |
| Model selector + destination | Makes the execution target, cost and privacy visible; supports quick switching. | Last allowed configured model; native dropdown with destination readout. Setup lives in Settings. |
| Preview view and Render | Shows the actual prepared frame and submits it after review. | One prepared image and prompt for the chosen destination, with Render enabled after valid preflight. |
| Latest result/job | Supports progress, review, save and recovery. | Shows source capture association, not a new render environment. |

Use the current view by default and reuse Rhino's own Named Views. Every representation aims to preserve the view, architecture and material assignments. Source comparison helps the designer identify deviations. [Capture and export sizing](../adr/0023-linked-export-dimensions.md)

## Prompt presets

**Implemented starting points for OpenAI; personal preset editing/saving remains a target.** Use a native **Preset** dropdown in the main panel, separate from **Model**. Preset chooses the intended representation; Model chooses the engine that produces it. A designer can choose a familiar starting point without writing the same instructions on every render.

Start with **Photorealistic**, **Competition Sketch**, and **Architectural Sketch**, alongside the existing Concept, Section Perspective, Material Study and Custom starting points. **Modern Illustration** is a possible later visual treatment to refine with examples; it must not ask the model to redesign the building in a modern architectural style. Keep the initial list small and understandable. Existing editable sample data lives in [examples/presets](../../examples/presets); its current Realistic entry is the starting point for Photorealistic, not a validated photorealistic result.

Each preset has a readable name, a short description and editable representation instructions. **Edit preset…** exposes those instructions; **Save as new preset…** creates a named personal version while retaining the supplied starting point. Personal presets live in user settings, outside `.3dm` and Git unless deliberately exported or shared. Keep the optional **Prompt** field for this render's additions. Switching presets changes the preset instructions without clearing those additions. No chat transcript or separate prompt workspace is needed.

Compose the chosen preset, optional user prompt, supported scene context and the shared geometry-preservation objective. Prompt editing does not remove that objective. All presets aim to retain architecture, camera composition and section geometry; Photorealistic refines the visible materials and lighting, while Competition Sketch changes their representation. Presets do not silently replace assigned Rhino materials, alter the scene, change model or resolution, or promise deterministic fidelity. Keep the complete composed prompt inspectable in Advanced.

Presets are provider-neutral intent. On a model switch, retain the chosen preset and check its requirements against the adapter; explain unsupported behavior instead of selecting another style. On submission, record the preset identity/revision and actual instructions with the job. Editing or saving a preset later must not change a running job or its provenance. The native Preset dropdown, optional prompt and recorded prepared-prompt inspection are implemented. Personal-preset editing/saving remains future work; shipped JSON can be edited in the repository and rebuilt.

## Contextual information

Section presence, unavailable requested treatment, native/final size differences, estimated cost if reliable, and outbound destination are shown when relevant. Unsupported requested behavior is never hidden inside Advanced. Vegetation interpretation can appear as a discrete architectural choice once its supported behavior is explainable; preserve position, scale and mass in the default sketch instruction.

## Scene guidance and details

Collapse Scene guidance by default. It summarises the inputs used by the selected profile and offers **View full prompt…** once a preview is prepared. Keep native canvas and export processing details available when they matter. Use Rhino to set display mode, materials and clipping. Keep local sampler, steps, seed policy and quantisation in the profile and job record. Expose new controls when they enable a clear designer choice with a supported model. [Local input contract](../development/model-selection-plan.md#local-model-controls-and-scene-guidance)

## Settings and history

Settings owns engines, approved endpoints, credentials, privacy policy, local storage and retention. History owns external job/result records and exports. Rhino continues to own material and view editing. Opening settings or changing a future default does not mutate a submitted job.

## Future Library

Render remains the primary workspace. A future [Library](library.md) groups curated **Assets** and **Materials** for preparing the Rhino document. Keep the browser separate from the capture controls and retain Rhino ownership of imported objects, blocks and materials. A tab within VisoLoom and a separate Rhino dock are both candidates; defer navigation and panel changes until there are usable resources to test.

## Model choice and connection settings

The main panel has one native **Model** dropdown for ready choices on **This computer**, **Your server**, and **Cloud**. Label the model and origin so hosted and local versions are distinguishable. The destination stays visible beside or below the selector. Switching is local UI state and happens before preview or submission. The existing development panel can select OpenAI, the capture test and the currently activated local model.

The target Settings layout is one native page with four sections. Keep each action in its section and show its result there. Preserve the current setup when reorganising these controls. The [wireframe](panel-wireframe.md#model-selector-and-settings) specifies the layout and common states.

- **Local models** shows installed choices, **Browse models…**, and **Use existing files…**. The catalogue rows show model name, hardware fit, size where known, and **Download** or **Get weights…**. Show the model folder and a chooser. The managed runner starts on demand. Display availability or recovery in the affected row. Choose the render model in the Render panel.
- **Cloud providers** groups OpenAI, Google, xAI and Black Forest Labs as their integrations become usable. Each provider opens its own inline setup with a masked key field, **Remember API key**, **Connect**, and the appropriate disconnect action. An existing key is represented by its connection and storage state, such as **Connected · Key saved** or **Connected · This session only**. No entered secret appears in the page's summary. Show the destination and outbound inputs where the user grants permission or submits.
- **Your server** holds the named server, HTTPS address and any access token under **Add server…** or **Manage…**. A check authenticates and reads capabilities and leaves the user on that row. Keep server network setup and administration with the operator.
- **Image saving** shows automatic history storage and a compact optional additional-copy folder chooser. Keep this preference with the existing image-saving state.

Connection checks use authentication and capability requests without uploading an image or running a generation. Saving credentials and choosing a model are separate actions. An offline or disconnected pinned choice stays visible with a reason and recovery action.

After connecting, Settings shows the compatible image models under that provider's **Manage…** row. Refresh availability on connection or key replacement, and offer **Refresh models** for providers with account discovery. List only models with a reviewed image-edit profile that the account can access. For Black Forest Labs, check the key and display the curated hosted profiles. Use that same list as the cloud choices in Render, where the designer selects the model for the next image.

After connection setup, switching between ready models takes one selection in the main panel. Keep the user's prompt, representation and requested resolution. Revalidate them against the selected model's capabilities. For an unsupported intent, explain the conflict and show available choices before enabling Render. Show meaningful differences between the requested export and the provider canvas before submission.

Project policy filters allowed destinations. A local-only project cannot select a cloud model. A newly configured remote destination requires the existing explicit opt-in and payload disclosure before its first submission; fast switching never bypasses that policy. Approved destinations remain visible without asking the user to repeat unchanged setup on every render.

The running job and its result keep the original provider, exact model identity and version, captured intent and processing plan. Credentials belong to the OS credential store when supported and otherwise stay in the session. Saved connection preferences are user settings. The project pin stores only the explicitly saved model identity.

Both the existing Rhino Options/Preferences entry and the panel shortcut should use the same Settings control. Nonsecret preferences and model library locations persist per user. Current key storage and model pins already exist. Inline sections, additional provider connections and managed downloads are the next implementation work.

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

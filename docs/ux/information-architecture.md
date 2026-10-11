# Information architecture

Status: accepted interaction target. Companion documents: [principles](product-principles.md), [journeys](user-journeys.md), [wireframes](panel-wireframe.md), [native interaction](rhino-native-principles.md).

## Primary panel

| Element | Why it exists | Default / inferred information |
| --- | --- | --- |
| View readout | Prevents representing the wrong composition. | Active Rhino view and projection; no editable camera fields. |
| Mode | Chooses the kind of architectural representation. | Last compatible preset, initially Competition Sketch for an implemented generative engine. |
| Prompt | Adds project-specific intent that cannot be inferred safely. | Empty, optional multiline field; no chat transcript. |
| Resolution | Controls a consequential output requirement. | Viewport pixels; 2×, 4× and aspect-locked Custom. |
| Engine/model + destination row | Makes cost/privacy and selected execution target visible. | Saved allowed choice with Change link; not a mandatory per-render selection. |
| Render | Submits the visible intent and current Rhino snapshot. | Enabled only after valid preflight. |
| Latest result/job | Supports progress, review, save and recovery. | Shows source capture association, not a new render environment. |

The view is a readout, not another required selection. No fidelity slider: providers do not share an established architectural scale. Every representation mode aims to preserve geometry. Mode changes the representational treatment while source comparison helps identify architectural deviations and communicate model limits.

## Contextual information

Section presence, unavailable requested treatment, native/final size differences, estimated cost if reliable, and outbound destination are shown when relevant. Unsupported requested behavior is never hidden inside Advanced. Vegetation interpretation can appear as a discrete architectural choice once its supported behavior is explainable; preserve position, scale and mass in the default sketch instruction.

## Advanced

Collapse source display-mode override, pass availability/usage, effective prompt, explicit native size and processing plan, seed if supported, provider-specific parameters, material/semantic context summary, and sanitized diagnostics. Only expose implemented controls. Do not display raw JSON as the primary configuration experience.

## Settings and history

Settings owns engines, approved endpoints, credentials, privacy policy, local storage and retention. History owns external job/result records and exports. Rhino continues to own material and view editing. Opening settings or changing a future default does not mutate a submitted job.

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

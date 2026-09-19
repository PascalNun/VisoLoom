# Prompts, mappings, jobs, and results

Status: proposed for architecture approval.

## Prompt and preset data

An editable preset document has its own format version, stable ID, revision, display name, base instruction, preservation intent, vegetation policy, preferred/required pass list, optional provider preferences, and output defaults. Provider preferences never change destination silently. Preserve an exported preset snapshot with each job so later editing does not rewrite history.

Store Competition Sketch, Architectural Sketch, Realistic, Material Study, Section Perspective, Concept, and Custom as data when implementation begins. The supplied Competition Sketch text belongs in that data, including the distinct vegetation/location/mass and architectural geometry instructions. A prompt's request for exact preservation remains an instruction, not a measured guarantee.

Compose distinct layers in deterministic order: preset instruction, architectural preservation rules, user prompt, material context, semantic context, section context, then adapter formatting. Preserve the user's original text separately. Explain that model instructions are best effort and that a conflicting user request may undermine representation fidelity; VisoLoom should not silently rewrite an architect's prompt. Hard product invariants such as no camera mutation and allowed network destinations are enforced in code, not prompts.

Geometry-preservation rules remain part of prompt composition across representation modes. A prompt requesting architectural changes creates an explicit conflict with the preservation objective; do not silently interpret that conflict as permission to relax the objective. Keeping the original prompt and surfacing a known conflict preserves the architect's authorship without presenting redesign as successful representation.

Metadata-derived strings are bounded, escaped/delimited as data, and contain no secrets. Avoid narrating every object; include relevant deduplicated material descriptions and semantic aggregates, with explicit unknowns. Record any truncation. Advanced shows the effective prompt and omitted context. Do not display provider credentials or raw signed URLs there.

## Material and semantic mappings

Source Rhino material IDs and resolved representational descriptions are separate records. Do not edit the Rhino material when a description is supplied. Material identity describes appearance; semantic identity describes role. A material can span several roles and layers. Keep assignment bindings by object/instance path/subcomponent instead of placing one layer field on each material.

Proposed description precedence: explicit object/subobject override, explicit material mapping, nearest explicit layer mapping, project default, office default, then a cautious inferred description. Semantic precedence is separately explicit object/subobject tag, explicit layer/rule assignment, material rule, then inference/unknown. Manual assignments win over inference. Record the winning rule/source; do not concatenate contradictory overrides. Resolve equal-priority rule conflicts visibly.

Use stable source IDs where available and preserve nested instance paths. Renaming a material must not delete its description. Deletion leaves an orphaned mapping available for review; it must not attach to a newly reused table index. Unsaved documents use a session key until an explicit persistent association exists. Office libraries and the full correction UI are future work; ordinary unmapped models remain valid.

## Jobs

One immutable `RenderIntent` captures preset revision, user text, selected provider/model, output request, and any explicitly requested representational behavior. A `RenderPlan` captures capability revision, pass decisions, outbound disclosure, resolution transforms, and expected output. A `JobRecord` links both to an immutable Scene Package and tracks progress.

Local phases: preparing → capturing → uploading → queued → rendering → downloading → postprocessing → completed. Failures or cancellation can occur in any phase; no fake percentage is invented when a provider reports only stage status. Queueing before capture must not silently change which later view is captured; the first UI permits only one submitted user job at a time.

`cancelRequested` is separate from `cancelled`. Before submission, cancellation stops local work at safe boundaries. After submission it follows adapter capability: confirmed cancellation, best effort remote request, or stop waiting. If inference cannot be stopped, say that it may continue and incur a charge. A lost connection retains the job handle and supports reconnection. Retry of a failed completed attempt creates a linked new attempt; retry of an ambiguous submission uses safe idempotency/reconciliation.

Jobs outlive panel instances. Document closure cancels unfinished capture, detaches host references, and lets already-submitted work retain its external history. A result notification must not activate a different document, restore a camera, or change the model.

## Results and local history

Results include scene ID/version/hash, document-session association, captured view, provider/model and adapter versions, preset snapshot, original/effective prompt, requested/native/actual/final dimensions, exact processing history, timestamp, optional seed, consumed/omitted passes, and errors/warnings. Retain the source preview, original generated asset, and exported image when processing occurs.

Store this outside `.3dm` in private application data; allow explicit export to the user's chosen folder with PNG as the initial format. Do not overwrite an existing file without the ordinary save-dialog interaction. Recent history is lightweight and bounded; user exports are never cache eviction targets. Optional project/office storage location and retention policy must be explicit before enterprise use.

# VisoLoom roadmap

The local slice proves the scene and service boundary. One OpenAI editing adapter and native Settings are implemented; the user has tested real generation and PNG export successfully. Named Views, custom native display styles and two-point capture have initial Mac host evidence. UX and geometry preservation guide the next work; the phases are checkpoints, not recurring approval steps. See the [implementation review](development/phase-4-review.md) for remaining host and appearance checks.

## Next: validate capture and everyday use

Run the [Rhino acceptance checklist](development/plugin-smoke-test.md) on supported Apple Silicon and Windows hosts. Observe the first connection, view and resolution readouts, capture, source/result comparison, cancellation, reconnect and save. Test architectural sections, clipping, material inheritance and instance visibility. Measure pauses and memory at larger sizes. Fix confusing interactions and extraction gaps before adding more controls.

Continue validating the user's custom rendered display style with ambient occlusion and edges, which is now supported for capture. Its frame has initial native evidence; visible textures, line widths, AO, sun/scene shadows and clipping need broader appearance checks. Include deliberately chosen material tones and real-world texture scale in authored fixtures; aim for the desired appearance in Rhino before image editing. See the [reference-image strategy](architecture/proposed-architecture.md#reference-image-and-visible-intent).

## Proposed next UX work: preview the source before sending

Keep the current Rhino display style as the default. Do not label another style as producing the best result without matched scene comparisons. A future optional VisoLoom style could provide restrained edges, materials and lighting, while leaving custom Rhino styles available.

Prioritize an explicit local **Preview capture** action and one source preview beside the view/frame controls. Show the chosen view, display style, capture dimensions and any model padding or scaling before submission. Render should use the reviewed capture and prepared prompt rather than silently recapturing. Changes that affect that preparation must clearly require a refreshed preview. This is proposed work, not implemented behaviour. Browsing a view must remain read-only; showing a Named View stays an explicit Rhino action.

Keep the compact view selector and Rhino's Named Views browser initially. A thumbnail chooser is feasible as a later native UI, but thumbnail retrieval or generation needs a validated path that does not move the user's camera. Browsing thumbnails communicate view identity; they do not replace an up-to-date capture preview.

Custom frame ratios need a visible frame preview before capture. The current implementation preserves the visible viewport's exact aspect ratio. A safe-frame overlay and a saved output-size setting alongside a Rhino Named View are possible follow-up work, not current independent frame support. Do not introduce a second camera system or silently crop the reference.

## Proposed context review

The OpenAI adapter currently sends one prepared image and a composed prompt with bounded camera, frame, units, clipping and material summaries. Layer/material names are opt-in. The local reference server is a capture test, not a FLUX image integration; its receipt of a manifest is not evidence that FLUX would use every field.

The current material colour comes from Rhino's legacy `Material.DiffuseColor`, stored as `BaseColorSrgb`. It is not a texture average or a resolved PBR appearance. Material summaries describe eligible objects, including potentially off-screen or occluded ones, and have no proven pixel mapping. Preserve the image as the appearance authority; do not turn these colours into replacement instructions.

Evaluate a concise OpenAI prompt against the current detailed context on the same representative scenes. Keep the complete scene snapshot and numeric camera/frame information locally for capture validation and history regardless of which fields the provider prompt uses. Test explicit preservation instructions and supported, spatially meaningful context before assuming that more JSON improves fidelity. Proposed prompt reduction must refine the existing [first OpenAI adapter decision](adr/0012-first-openai-image-edit.md) explicitly; no current upload fields have changed. Define future local-model conditioning around the actual endpoint's capabilities rather than sending the same context by default.

## Next: validate the first real image integration

Use the implemented OpenAI adapter with explicit cloud opt-in and secure credentials. Build on the successful first real requests before broadening provider scope. Benchmark openings, stairs, section voids, proportions and complete-frame composition separately from visual style. Preserve architecture in every representation mode; vegetation interpretation may change its drawn appearance while retaining its location, scale and overall mass. A model's nondeterminism does not relax that objective.

See the [implementation review](development/phase-4-review.md#recommended-first-real-image-integration) for the current first-provider recommendation. Add aligned structural passes only where the chosen adapter can meaningfully consume them. Sending an arbitrary extra image does not establish spatial control.

Benchmark the custom AO/edge capture against a matched view with restrained linework and deliberate lighting. Add a completed conventional-render comparison only once its input path is validated. Measure material appearance, planting character and shadow retention alongside geometry; do not assume one display style is best for every model or representation. Full lighting provenance and complex material/texture extraction need separate implementation work.

## Rhino material collection

**Planned, not implemented.** Add a curated material collection alongside the geometry assets. Materials must look useful in Rhino before image generation: a concrete finish should already communicate its color, grain, roughness and scale. A short description supplies additional architectural intent, such as fine board marking or warm mineral render. Avoid an appearance workflow based on arbitrary placeholder colors plus material labels.

Use ordinary Rhino render materials and texture mappings. A small browser may provide discovery and an explicit **Add to Rhino** action; assignment and subsequent editing stay in Rhino's Materials panel. Imported materials remain editable and useful without VisoLoom. Users can use their own materials and any suitable existing libraries without catalogue or quantity restrictions. Assigning or replacing a material is an intentional model edit with Rhino undo; capture never reassigns it. Rhino already supports [material-library import and PBR creation from texture files](https://docs.mcneel.com/rhino/8mac/help/en-us/commands/materials.htm); evaluate native `.rmtl` content before inventing another format or editor.

Begin with a few differentiated concrete finishes, plaster, timber and planting materials used by the first asset fixtures. Include clear real-world mapping guidance and useful editable defaults. Check repeat seams, texture resolution, color, roughness/bump where supported, transparent foliage and performance in the chosen capture mode. Preview the actual Rhino appearance. Verify texture packaging, relinking and save/reopen behavior before distributing the collection.

Record source, creator, revision, adaptations and redistribution rights for every material and texture set, following the asset rights process below. No textures, thumbnails or material library have been downloaded or bundled by this plan. The captured image may show textures; sending their raw maps or separate reference swatches remains a distinct, explicitly supported and authorized provider operation.

## Lightweight asset browser

**Planned, not implemented.** Build a purpose-built asset library whose objects already look close to the intended result in Rhino. Lightweight means optimized, recognizable 3D assets with useful geometry and materials. A tree should already read as the chosen kind of tree; a shrub should have credible shape, volume and foliage. The image model refines this visible appearance using extra information. Begin with plants: trees, shrubs and low planting. People and furniture can follow once the placement and depiction workflow works well.

Creating that library is substantial product work alongside the browser: asset creation or sourcing, geometry cleanup, materials, variations, optimization, thumbnails, rights records and visual acceptance. Grow a coherent VisoLoom collection over time, rather than assuming a few generic symbols can supply enough information for a reliable rendering.

### Interaction

Use a small native browser opened from the panel, with search, a few categories, clear thumbnails and **Insert**. The designer chooses a plant and an appearance or growth variation, places it in Rhino, and uses ordinary Rhino move, rotate, scale, layers and block tools afterwards. Assets remain useful when VisoLoom is closed. Keep this out of the primary capture controls until it is needed.

Use shared, embedded Rhino block definitions containing optimized meshes with credible silhouette, branching, canopy and foliage distribution. Use ordinary Rhino materials to establish color and surface appearance. Define real units and a useful ground-level insertion point. Reuse one definition across instances. Start with a handful of predictable variations; record the selected variation rather than randomizing it on each render. Evaluate transparency, leaf cards and billboards separately because their appearance and section behavior depend on the view and display mode. This is a proposed design based on Rhino's existing [block workflow](https://docs.mcneel.com/rhino/8mac/help/en-us/commands/block.htm).

Insertion is an intentional model edit and must participate in Rhino undo. Capture remains read-only. Do not add a second camera, material editor, placement system or separate scene database.

### Information for the image model

Give each asset a stable identifier and revision. Store a short description with the definition, and selected species, growth, appearance and variation with each instance. Prototype the editable instance values through Rhino Attribute User Text, which Rhino already uses for [block attributes](https://docs.mcneel.com/rhino/8mac/help/en-us/commands/converttexttoblockattribute.htm). Confirm how definition metadata, copies and instance overrides survive editing before fixing the storage format.

Extend scene extraction to resolve asset instances and their transforms. Package only the visible reference, bounded descriptions and any validated aligned control passes the adapter uses. Do not upload asset meshes, local paths or the complete `.3dm` by default. User-authored asset descriptions follow the same privacy rules as layer and material names.

The Rhino asset supplies **placement, scale, form and a first visual appearance**. Its description adds information such as species, growth, leaf character and season. The captured asset should already be a useful reference before any image generation. Text alone cannot reliably bind a plant description to a particular pixel region. Evaluate aligned object/semantic masks and projected bounds, with explicit provider support. Test placement, scale, variety and overlap in the final image. The model should aim to retain the selected species and appearance as well, but these are benchmark criteria, not guarantees. Asset depiction must not authorize invented architecture or unintended changes to context.

The current slice does not yet resolve nested blocks or capture asset-specific metadata. This plan requires that extraction work; it is not a claim that the existing adapter already supports it.

### Assets and rights

Begin with a small, locally available collection of finished assets with clear provenance, then expand it into a larger consistent library. Sources can include original modelling, suitably licensed existing assets and AI-assisted creation. Keep a record per asset: creator, source or generation process, revision, units, dimensions, mesh size, license evidence and adaptations. Set file-size and triangle budgets after measuring a planting-heavy scene. Generate our own browser thumbnails from the actual Rhino assets.

AI-assisted creation must produce usable 3D geometry and materials. A generated image can be a design reference, but it is not an insertable Rhino asset. Evaluate a separate mesh-generation or reconstruction workflow, then clean up geometry, simplify detail, repair normals, establish units/pivot and prepare materials. Review its tool, model and output terms before choosing a service. Keep that production workflow outside the lightweight Rhino plug-in; no asset-generation runtime is included now.

Whether created manually, generated or sourced, every asset needs checks in Rhino: recognizable appearance without image generation, several camera angles, expected scale, correct normals/materials, clipping, save/reopen and acceptable performance with many instances. Preserve the detail that communicates the asset's character when simplifying it. Image-model enhancement is an additional acceptance test after these basics work.

[Poly Haven](https://polyhaven.com/license) is a candidate source for assets or reference: its asset license is CC0, while website content and access terms are separate. Evaluate individual models for suitability and simplify them where useful; do not assume every source model is lightweight. A future live connection must separately review its [API terms](https://github.com/Poly-Haven/Public-API/blob/master/ToS.md). Asset rights, API access and the license of a provider's code are separate questions. No external asset collection or API integration has been added.

Keep asset rights independent from the undecided VisoLoom software license. Do not copy restricted catalogue thumbnails or bundle assets whose redistribution rights are unclear. No new runtime dependency is needed to record this plan.

### First asset milestone

Produce three recognizable plant types with a few variations, including their actual geometry and Rhino materials. Include a small set of representative architectural materials on adjacent building surfaces. Test one tree, a row of trees and dense mixed planting in perspective, parallel and section views. Assess the unprocessed Rhino appearance first, then compare image-model refinement. Compare model weight, placement speed, ordinary Rhino editing, undo and fidelity. Use the results to choose the browser layout, asset detail budget, metadata contract and control-pass strategy before expanding the catalogue. Plan asset and material production as sustained work.

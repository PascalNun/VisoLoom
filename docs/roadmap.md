# VisoLoom roadmap

The local slice proves the scene and service boundary. One OpenAI editing adapter and native Settings are implemented; real generation and its host UX still need validation with an API key. UX and geometry preservation guide the next work; the phases are checkpoints, not recurring approval steps.

## Next: validate capture and everyday use

Run the [Rhino acceptance checklist](development/plugin-smoke-test.md) on supported Apple Silicon and Windows hosts. Observe the first connection, view and resolution readouts, capture, source/result comparison, cancellation, reconnect and save. Test architectural sections, clipping, material inheritance and instance visibility. Measure pauses and memory at larger sizes. Fix confusing interactions and extraction gaps before adding more controls.

Prioritize the user's custom rendered display style with ambient occlusion and edges. It is currently rejected by the first slice's display-mode gate. Validate its exact frame, visible textures, line widths, AO, sun/scene shadows and clipping before enabling it. Include deliberately chosen material tones and real-world texture scale in authored fixtures; aim for the desired appearance in Rhino before image editing. See the [reference-image strategy](architecture/proposed-architecture.md#reference-image-and-visible-intent).

## Next: validate the first real image integration

Use the implemented OpenAI adapter with explicit cloud opt-in and secure credentials. Run its first real request before broadening provider scope. Benchmark openings, stairs, section voids, proportions and complete-frame composition separately from visual style. Preserve architecture in every representation mode; vegetation interpretation may change its drawn appearance while retaining its location, scale and overall mass. A model's nondeterminism does not relax that objective.

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

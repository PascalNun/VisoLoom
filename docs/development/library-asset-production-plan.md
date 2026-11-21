# Library asset production plan

Written 2026-11-21 as the production plan for [Track 9](onboarding.md#track-9--visoloom-library-asset-browser--material-collection) of the onboarding inventory. The [roadmap](../roadmap.md#lightweight-asset-browser) and the [Library UX plan](../ux/library.md) keep the direction and the browsing/insertion interaction targets; this plan sequences the engineering prerequisites and the first asset-production milestone so work can start without re-deriving them. Evidence language follows the repository rules: everything here is **planned** — no asset, material, browser or extraction code exists at 2026-11-21 — and the browser UI decision stays deferred until the collection exists, per the UX plan.

## Engineering prerequisites

Two changes the current slice does not have. Both precede or accompany asset production; asset metadata is useless until the extraction contract carries it, and the assets cannot pass acceptance until insertion is reliable.

### P1 — Asset-aware scene extraction

The phase-4 review records that the current slice does not resolve nested blocks or capture asset-specific metadata. Production needs:

- Resolve block instances (including nested definitions) to their transforms, so a planted tree reads as "this definition, at this transform, with this scale" rather than anonymous geometry. Resolution stays within existing extraction bounds and the privacy rules: asset descriptions follow the same opt-in treatment as layer and material names, and nothing uploads meshes, local paths, or the `.3dm`.
- Extend the scene-package tables with a bounded asset pass only when the extraction has something true to put in it; [ADR 0013](../adr/0013-named-view-capture-controls.md)'s read-only capture rules apply unchanged — capture never reassigns a material, and insertion remains an explicit, undoable Rhino action.

### P2 — Asset metadata contract

- Stable asset identity and revision stored with the block definition; the short description, species/growth/appearance fields and per-instance selected variation stored through Rhino's Attribute User Text, which Rhino itself uses for block attributes. Confirm how definition metadata, copies, nested reuse and instance overrides survive editing, redefinition and save/reopen before fixing field names — that verification is the first fixture, before any UI assumes it.
- The selected variation is **recorded, never randomized** (roadmap rule): each render's provenance names what was planted, so a repeated render can be compared against its recorded scene, not a re-rolled one.

## Asset production pipeline

For each asset, in order:

1. **Sourcing decision** (see sources below; one decision per asset, recorded in the rights record).
2. **Geometry:** units and a useful ground insertion point; correct normals; a silhouette and foliage distribution that reads as the intended plant without image generation. Detail budget from the roadmap: recognizable without VisoLoom, several camera angles, performant in a planting-heavy scene — measured before any budget becomes a rule.
3. **Materials:** ordinary Rhino render materials with texture-mapping guidance and real-world scale; transparent foliage and leaf cards stay a separate evaluation (their section and view behaviour differ).
4. **Variations:** two to three per plant type, each an explicit variation of the shared definition, not a new ad-hoc model.
5. **Thumbnail:** generated from the actual Rhino asset — never an unrelated promotional image.
6. **Acceptance in Rhino first** (roadmap checklist): recognizable at several angles, expected scale, correct normals/materials, clipping behaviour, save/reopen — including on a machine without VisoLoom — and many-instance performance.
7. **Rights record** (per asset: creator, source or generation process, revision, units, dimensions, mesh size, licence evidence, adaptations), before the asset enters any collection.
8. **Image-model refinement test, last and optional:** an additional acceptance, never a substitute for the Rhino basics; the roadmap's rule that the captured asset must already be a useful reference stands.

### Sources, with today's licence facts

- **ambientCG — CC0 1.0** (checked 2026-11-21: [licence page](https://ambientcg.com/licenses)); its concrete/plaster/timber texture categories are primary candidates for the materials collection. Per-asset suitability (repeat seams, resolution, appearance in Rhino's renderer) still reviews individually — CC0 is a rights fact, not a quality judgement.
- **Poly Haven — CC0** for the assets themselves, with a separate website/API terms-of-service question the roadmap already records (checked 2026-11-21: [asset licence](https://polyhaven.com/license)). Its 3D model inventory is realistically small and must be judged per asset at sourcing time; treat it as a possible incidental source, not a plant catalogue.
- **Original modelling and AI-assisted creation** — the roadmap's expected main path. A generated image can be a design reference, but the insertable result is real geometry and materials, produced and cleaned outside the plug-in (no asset-generation runtime ships with VisoLoom).
- **Commercial plant libraries** (Laubwerk/SpeedTree class): paid licences that would need their own terms and budget review; an open decision, not a default.

An honest gap, recorded rather than papered over: photoreal, Rhino-ready, permissively licensed plant models are scarce. The first milestone should expect original/AI-assisted production, which is also the roadmap's stated direction — "substantial product work", planned here as sustained work, not a weekend download.

## Materials sub-milestone

Concrete finishes, plaster, timber and planting materials on adjacent surfaces, using ordinary Rhino render materials built from CC0 texture sets (ambientCG candidates first) or self-authored textures, each with mapping guidance and usable editable defaults. Evaluate Rhino's native `.rmtl` material export/import before inventing any format ([Rhino materials documentation](https://docs.mcneel.com/rhino/8mac/help/en-us/commands/materials.htm), as the roadmap already requires). Check repeat seams, resolution, colour, roughness/bump where supported, and the actual Rhino appearance — "materials must look useful in Rhino before image generation" is the roadmap's acceptance rule. Verify texture packaging, relinking and save/reopen before distribution, and keep the rights record per texture set. Sending raw maps or swatches to a provider stays a distinct, explicitly authorized operation — the capture-leading default never changes.

## First asset milestone

Mirrors the roadmap's definition, made sequenced and testable:

1. Three recognizable plant types with two-three variations each, plus the adjacent architectural materials above.
2. Test scene battery: one tree, a row of trees, and dense mixed planting — each in perspective, parallel and section views; transparent foliage and clipping checked deliberately.
3. Judge the **unprocessed Rhino appearance first**; only then the image-model refinement, scored against the fidelity protocol, not stylistic preference.
4. Record per asset: triangle count, file size, placement speed, undo behaviour, variation fidelity, and (per P1/P2) whether the recorded metadata survived every operation. These numbers, not a guess, set the detail budget and the data shapes both candidate browser UIs need.
5. The browser UI decision ([library plan](../ux/library.md), two options) follows these results and stays out of this milestone.

## Release-check fixtures (when code lands)

Nested-instance resolution in scene extraction; user-text survival across definition edits, copies and save/reopen; the selected variation reaching per-render provenance exactly as planted; insertion and assignment participate in Rhino undo; definition reuse without silent replacement of an existing block; browsing creates no generation, no credentials use, and no automatic upload of geometry, textures or catalogue metadata.

## Open decisions

1. Sourcing strategy: original/AI-assisted production (this plan's default) versus buying commercial plant libraries — quality risk versus cost.
2. Texture-set approval for the materials collection (ambientCG/Poly Haven candidates named above).
3. Asset-quality acceptance — the Rhino walkthrough is hands-on by nature; the assets must look right in Rhino, unprocessed.
4. The browser UI decision, after the first milestone's results.
5. Image-model refinement acceptance criteria per representation mode, alongside Track 2's comparison protocol.

## What this plan does not decide

Browser layout or panel placement (the UX plan's two options stay open); the asset-model conditioning strategy (the roadmap's masks/bounds evaluation happens with real assets); any licence or rights question for VisoLoom itself (the [software licence memo](../legal/software-licence-decision.md) keeps that separate); and no asset, texture or thumbnail is downloaded, committed or bundled by this plan — the roadmap's rule stands until sourcing is approved.

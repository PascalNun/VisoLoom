# Safe-frame framing implementation plan

Written 2026-11-21 to sequence the roadmap's framing section — independent output ratios inside a centered on-screen frame — into an implementable increment. The [roadmap's framing paragraphs](../roadmap.md) hold the accepted direction and [ADR 0023](../adr/0023-linked-export-dimensions.md) the implemented linked-dimension behaviour; this plan adds the sequencing, the open design decisions and the validation matrix, changing nothing by itself. Evidence language follows the repository rules: everything here is **planned** — no overlay, capture or panel code for the frame exists at 2026-11-21, as the [panel wireframe](../ux/panel-wireframe.md) records.

## Current state

- Custom sizes are linked with nearest-pixel rounding ([ADR 0023](../adr/0023-linked-export-dimensions.md), implemented): commits keep the edited dimension and round its companion; unrelated aspect changes are rejected; captures stay at the camera's exact aspect; no crop or camera controls exist.
- The capture pipeline already records a fractional sampling rectangle with a single uniform scale when a complete source aspect cannot fit as whole pixels, with a mathematical framing harness and a native landmark check (check-local-framing) for the fractional path.
- The preview is the actual model canvas including recorded padding; no safe-frame overlay exists.

## Target interaction

**Lock to viewport ratio** joins Custom, on by default — the ADR 0023 behaviour unchanged while locked. With the lock off, both dimensions are typed freely, their aspect ratio is shown, and the chosen ratio draws as a centered safe frame inside the selected Rhino view. The first version frames a *subset* of the visible view, with the excluded edges visibly marked; Rhino's own Safe Frame is the interaction reference. The designer composes with Rhino's existing viewport or Named Views — repositioning happens in Rhino, never in the panel. The full-view-fit-with-borders variant stays a separate future evaluation, as the roadmap already scopes.

## Open design decisions (to be recorded in a new ADR at implementation)

1. **Overlay drawing.** A Rhino display conduit draws the centered frame rectangle in screen space of the selected view, refreshed on view or dimension changes, only while the panel holds a lock-off ratio. The roadmap's hard rule stands: the overlay must never reach the source image — either the conduit disables itself for the capture call or the capture path ignores non-document draw phases; the decision records which, with the check that proves it.
2. **Capture mechanism.** Two candidates, both prototyped on the original Rhino 8.0 SDK as the roadmap requires: direct window capture of the frame region (risk: projection and display-appearance changes) versus the validated viewport capture with the known frame rectangle extracted and uniformly scaled (reuses the verified capture path and the existing fractional-scale concept). The expected outcome is the extraction path; evidence picks.
3. **Frame math.** The centered rectangle at the output aspect in device pixels, DPR-corrected; its pixel grid alignment and the target canvas follow the existing resolution planner — the engine's 64-pixel grid and provider size sets stay the planners' business. One rectangle serves overlay, captured reference and comparison.
4. **ADR 0023 refinement.** With lock off, "unrelated aspect changes remain rejected" becomes "rejected without a frame; accepted with the frame, within the existing capture and export limits" — the explicit decision update, in the pattern the repository requires for refining an accepted ADR.

## Implementation steps

1. **Panel controls:** lock toggle, free-dimension editing under the lock, honest ratio label; no capture behaviour yet. Core tests for control states, validation and the ADR 0023-unchanged locked path.
2. **Frame math core:** rectangle-from-aspect in view coordinates; unit-tested against DPR cases, narrow views, degenerate ratios and named-view sizes.
3. **Overlay conduit:** visual only; native check that it tracks view changes and stays absent from saved captures.
4. **Capture extraction:** the decided mechanism behind the existing capture plan — capture first, then the frame rectangle extracted with a single uniform scale, recorded in the capture record alongside today's fractional sampling fields; protocol additions stay within the schema's minor-version tolerance rules.
5. **Preview and comparison:** the frozen preview contains exactly the framed pixels; the comparison viewer aligns the same rectangle to the result by the existing transform rules; automatic saving and inspection keep the two requested export dimensions through the unchanged planner.
6. **Native fixture growth:** extend check-local-framing with frame-specific landmark mappings and an overlay-visibility check.

## Validation matrix (from the roadmap, made checkable)

Perspective, two-point and parallel views × landscape, square and portrait frames × selected view and named views × clipped scenes. Assertions per cell: projected landmarks inside the frame map into the captured reference within the existing tolerances; the excluded region stays out of the reference; the document, camera and render settings compare unchanged (the two-point work's state-comparison machinery replays here); the overlay is absent from the source PNG; exports are sized to the two requested dimensions within the existing limits; the default-locked path behaves exactly as ADR 0023 does today.

## What this plan does not decide

The full-view-fit-with-borders variant (separately evaluated later); any crop handles or interactive reframe (the frame is driven by the typed dimensions and repositioned in Rhino); the framing overlay's final visual treatment (panel-wireframe conventions own appearance wording); model canvas recommendations; and no camera, provider or Settings-menu behaviour changes. Native acceptance — overlay walkthrough, narrow pages, themes, Windows — is hands-on Rhino work and lands with Track 1.

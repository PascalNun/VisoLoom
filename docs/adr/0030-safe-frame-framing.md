# ADR 0030 — Safe-frame framing inside the locked view

Status: Decided 2026-11-21 from the roadmap's framing direction and the [framing plan](../development/framing-safe-frame-plan.md); refines ADR 0023's linked-dimension rule. Implementation is sequenced by the framing plan; nothing here changes the locked path, and native Rhino acceptance stays hands-on.

## Decision

**Lock to viewport ratio** joins Custom, on by default. While locked, ADR 0023's behaviour is unchanged: the edited dimension keeps its typed value and the companion rounds to the nearest pixel, and Rhino captures keep the camera's exact aspect. Lock-off is an explicit choice per session; it drives a frame, never a camera or crop control.

With the lock off, both dimensions are typed freely and the panel shows the resulting ratio honestly. The chosen ratio draws as a centered safe frame inside the selected Rhino view — the largest centered rectangle at that aspect in device pixels, DPR-corrected, as a strict subset of the visible view with the excluded edges visibly marked. Composition happens in Rhino's viewport or Named Views; the panel never repositions the frame. The full-view-fit-with-borders variant stays out of scope, as the roadmap records.

**Overlay.** A Rhino display conduit draws the frame rectangle in screen space of the selected view, refreshed on view or dimension changes, existing only while the panel holds a lock-off ratio. The conduit is disabled around the capture call — an explicit disable by the capture path, not a reliance on draw-phase filtering — because viewport capture replays the display pipeline, and an explicit disable is deterministic. The overlay never reaches the source image; the native framing fixture asserts its absence from the captured PNG.

**Capture.** The capture mechanism is the validated viewport capture at the view's own dimensions, followed by extraction: the frame rectangle is taken from the captured viewport image and uniformly scaled to the typed dimensions, recorded in the capture record alongside the existing fractional sampling fields, within the schema's minor-version tolerance. Direct window capture of the frame region is rejected: it changes projection and display appearance, the two properties the existing capture path exists to keep stable. Never change the camera, stretch the view, or capture outside the verified path.

**Planning.** The frame rectangle is the content region of one shared record: overlay, captured reference and comparison all use the same rectangle. Typed dimensions are the final export, bounded by the existing capture (16,777,216 pixels), export (67,108,864 pixels) and per-side limits; the model canvas keeps following the resolution planner, whose grid rules apply to the canvas, not to the frame. ADR 0023's refinement applies exactly as the framing plan words it: with the lock off, unrelated aspect changes are no longer rejected — they are accepted with the frame, within the existing limits; without a frame they stay rejected.

## Consequences

The locked path needs no changes and its tests stay. The free path adds: the toggle and free-dimension panel controls with honest ratio labelling; the frame-rectangle core with its DPR, narrow-view and degenerate-ratio tests; the conduit; the extraction in the capture record; preview and comparison carrying the same rectangle; and the native fixture growth. The frame is visual and recordable, not a camera state: repositioning belongs to Rhino. The overlay's final visual treatment follows the panel-wireframe conventions. No provider, Settings-menu, engine or camera behaviour changes.

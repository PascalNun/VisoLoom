# ADR 0016 — Panel hierarchy and Named View browsing

Status: implemented following the user's request on 2026-10-13; native acceptance is recorded in the implementation review.

## Decision

Group the panel into View, Image, Output size, Preview and render, and Result. Keep Settings in the header, model switching in Image and the cloud destination visible before submission. Manual local connection details belong in Settings. Show custom width/height only for Custom, make the optional prompt collapsible, and hide empty preview/result areas. Native labels measure their wrapped height as text or width changes instead of retaining a one-line height.

Keep the compact source dropdown and add **Browse named views…**. The native browser reads Rhino's existing saved views and shows one local composition thumbnail at a time. Choosing a name changes only VisoLoom's source selection; **Show named view in Rhino** remains the explicit camera-restoration action. There is no independent camera manager, editing/renaming/deleting saved views, automatic restore on result completion or persistent thumbnail cache.

The thumbnail uses a copied camera and Rhino's raster capture settings, with its output dimensions supplied by the capture settings. Never set `RhinoViewport.Size` to prepare it: the Mac host can resize its parent window even for a viewport copy. Reject scene/view changes during preparation. The thumbnail is a navigation aid, not the reviewed upload or an independent saved-output frame. The exact input still comes from Preview capture after showing the selected view. Display-style, lighting and clipping fidelity need broader host checks; unavailable thumbnails retain selection and explicit Show as a fallback.

The browser performs no provider request, credential lookup, history write or model-file save. Rhino owns saved cameras and scene appearance; VisoLoom continues to use the selected display style. No new dependencies, auxiliary passes, provider adapters or safe-frame overlays are introduced.

## Validation

Compile against the original Rhino 8 SDK and run the existing automated suite. Use `tests/native/check-named-view-thumbnails.py` with two saved compositions to verify bounded PNGs and unchanged cameras, selection, tables, named definitions, modified state and undo counters. Inspect thumbnails and the panel at narrow/wide widths in a real Rhino host. A source harness is separate evidence from the registered/docked panel after restart.

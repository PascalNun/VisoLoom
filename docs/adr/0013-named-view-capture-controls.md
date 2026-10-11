# ADR 0013 — Named Views and capture controls

Status: implementation of the user's Named Views priority and continued-work authorization, 2026-10-12. Refines the initial display gate in ADR 0009 without changing provider, privacy or licensing boundaries.

## Decision

List Rhino's Named Views before open model viewports. Selection is read-only and sends nothing. **Show named view in Rhino** explicitly restores the selected saved view into the active model viewport using Rhino's existing API. Render reads the visible viewport afterward and never restores a view itself. Changing camera/framing, replacing the saved camera, renaming or deleting the saved view requires reapplying it or choosing the current view. Display-style edits remain allowed.

The visible viewport's current aspect/frame is authoritative. This does not provide independent offscreen capture at a saved render size or lock composition across viewport resizing. Those controls need a separately validated capture-frame design. Use saved names with existence/definition checks: newer named-view identity APIs are outside the original Rhino 8 SDK baseline. Never fall back silently from an unavailable saved or pinned view.

Open model viewports can also be pinned and captured without activating them. Capture binds the document and viewport and checks its camera, pixels and display-mode ID before/after capture. Camera metadata reads Rhino's effective screen-up basis (CameraY); CameraUp can be a non-orthogonal hint, even in its default templates.

Keep exact frame proportions. Link custom width and height to the nearest whole-pixel pair with exactly the source ratio. Show the resolved pair and rounding before capture, then distinguish capture/export size, native model canvas, padding and resized model content. Do not independently round dimensions or hide enlargement.

Allow regular native display styles, including custom AO/edge styles. Continue rejecting live render engines/Raytraced, two-point perspective and page/detail capture. The source style stays Rhino-owned; VisoLoom does not edit display definitions, lighting or materials. Custom styles carry an appearance-validation warning in scene metadata.

## Evidence and remaining checks

The original Rhino 8.0 SDK/net7 build passes. A harness linked to the production capture/named-view source ran inside Rhino 8.16.25042.13002 on this Mac under .NET 7.0.0. Nine authored captures cover Rendered and Rendered_LN_PN perspective/parallel views at viewport and 2×, an inactive Top view and explicit named-view restore. Frame sizes and view-specific hidden clipping metadata match; camera/frame, selection, table counts, modified state and undo counters are unchanged during capture. Selection, camera movement, rename/deletion and removed-viewport checks behave as intended.

The maintained [native check](../../tests/native/check-capture.py) reproduces this workflow after loading the latest plug-in. Reports and fixture images stay outside the repository. This is capture-source evidence, not a full plug-in-panel usability review or image-model geometry benchmark. Revised panel interaction after restart, independent saved framing, Windows, texture mapping, transparency, sun/shadows and the wider no-mutation matrix remain pending.

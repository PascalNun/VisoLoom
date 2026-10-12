# ADR 0014 — Two-point perspective capture

Status: implementation following the user's two-point view report, 2026-10-12. Refines the projection gate in ADRs 0009 and 0013.

## Decision

Capture Rhino's existing two-point projection through its native display pipeline. Do not convert it to ordinary perspective, recenter its frustum or reconstruct it from a lens length. Record `TwoPointPerspective` in the existing Scene Package camera contract and retain the asymmetric frustum, effective screen-up and complete frame. Use the same custom display style, clipping and capture limits as the other supported model views.

Named View restoration remains an explicit action before capture. Its saved-definition fingerprint includes the two-point flag, and its applied-camera comparison allows only numerical rounding (relative/absolute tolerance 1e-12). Projection and viewport pixels must match exactly. Visible camera edits and changes to saved definitions require reapplying the view. Capture/generation/results never restore or alter the source view themselves.

## Evidence and remaining checks

The local current-view probe ran inside Rhino 8.16.25042.13002 / .NET 7.0.0 on the user's two-point interior view. It captured 2358 × 1810 and 1179 × 905 with the intended custom rendered/edge style. All 25 sampled world landmarks matched their native capture pixel positions (maximum measured difference below 7e-12 pixels). Source camera/frame, selection, table counts, modified state and undo counters remained unchanged. The asymmetric frustum and vertical camera basis were retained. Captured appearances were visually inspected; images and camera reports remain private, outside Git. No provider call was made.

The first exact-number assertion caught a 3e-17 frustum rounding difference during smaller capture. The probe now compares floating-point values with a strict numerical tolerance and separately verifies projected landmarks within half a pixel. Rhino's `Viewport.Size` remains the source size during offscreen capture, while `WorldToClient` uses capture pixels; the probe normalizes those coordinates with the requested capture canvas.

The original Rhino 8 SDK/net7 build and 75 local tests pass. New regressions cover two-point/asymmetric-frustum JSON and prompt context, and rejection of camera, projection, pixel-size and meaningful frustum changes. A harness linked to the revised capture/Named View source passed the expanded authored native suite: 14 captures across ordinary perspective, parallel and two-point views, standard Rendered and Rendered_LN_PN, viewport/2× sizes, hidden clipping and inactive-view capture. It restored a saved two-point view from ordinary perspective and rejected a later projection change. The explicitly bound original user document compared unchanged at the end. An earlier final document comparison failed without recorded before/after values; the maintained check now records those values and changed fields for diagnosis. The successful rerun did not relax that comparison. Windows native capture, broader view/frame/graphics cases and revised panel interaction after restart remain acceptance work. This is input-capture evidence, not generated-image geometry preservation.

Native APIs: [ViewCapture](https://developer.rhino3d.com/api/rhinocommon/rhino.display.viewcapture/capturetobitmap), [RhinoViewport](https://developer.rhino3d.com/api/rhinocommon/rhino.display.rhinoviewport).

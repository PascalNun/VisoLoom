# ADR 0015 — Preview before submission

Status: Implemented following the user's request on 2026-10-12. Refines the submission workflow in ADRs 0006, 0012 and 0013; provider, privacy and preservation contracts remain in force.

## Decision

Add **Preview capture** as a local action. It captures the chosen visible Rhino view in its current native display style, freezes the scene and intent, plans the selected model's canvas and prepares the exact PNG for upload. Preview needs no API key or running service, performs no provider discovery, accesses no credential store and writes no history. Grid/axes suppression remains the existing capture policy; it does not change the document.

Show the prepared model input, including any surrounding padding, with its view, style, dimensions and capture time. **Enlarge preview…** opens a resizable native inspection window. **Review prepared prompt…** is available before submission. This first inspection window fits the image; pixel-level zoom and source/result overlays remain future work.

**Render** uses those same prepared PNG bytes, scene, prompt and plan. It does not recapture or transform the reference again. Connection/model discovery and existing validation still run before submission. Reject changed inputs both before discovery and immediately before sending. Save the prepared PNG and its hash alongside the original reference, intent and plan when a request is submitted.

View/camera/frame, selected source or Named View, model, resolution, preset, prompt, connection/privacy settings and observed scene edits expire the preview. Expiration is permanent; changing a setting back does not revive an earlier review. Submission consumes the preview, including a failed connection attempt. A deliberate new render requires Refresh preview, preventing an accidental duplicate billable click. Local reconnection retains its existing idempotent saved-job workflow.

Selection/browsing sends nothing. Showing a Named View remains an explicit Rhino action. Keep the designer's native display style; no “best style” is asserted. Custom ratios and safe-frame overlays are not introduced here: capture continues to preserve the visible viewport's exact aspect.

## Limits and evidence

The revision counter is conservative across open documents. Another document's observed edit can expire a preview. Same-ID display-style definition changes and externally edited texture files are not fully tracked; refresh explicitly after those changes. Complete content hashing or render-content resolution is separate work. Once submitted, the frozen job remains associated with its captured source even if the designer continues modelling.

Automated checks cover permanent expiration, single-use submission, exact reviewed PNG/prompt transmission, invalid canvas/source rejection before HTTP and no second reference transform. Native fixture and loaded-panel acceptance are tracked in the [implementation review](../development/phase-4-review.md). No additional image-model request was made for this milestone.

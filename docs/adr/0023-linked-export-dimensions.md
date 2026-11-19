# ADR 0023 — Editable dimensions and complete-frame export

Status: Implemented following the user's sizing feedback on 2026-11-19. Refines the exact-aspect custom-size control in ADR 0013.

Custom width and height are ordinary text fields. Commit on Enter or leaving the field; keep the edited dimension and round its linked companion to the nearest pixel. Polling must not replace partially typed text. A provider's 16- or 64-pixel grid applies to its input canvas, not the designer's export field.

Keep Rhino captures at the camera's exact aspect ratio. Capture at the requested dimensions when they preserve that ratio and fit the existing 16,777,216-pixel capture limit; otherwise capture the viewport at its original dimensions. Never change the camera or stretch the view to satisfy an export size.

Record capture, model canvas and final export sizes separately. The planner targets the requested output within the model's declared limits. Normalisation removes model padding and fits the entire image uniformly into the final canvas. Linked integer rounding can leave small borders; width or height must still be the rounded companion of the other dimension. Unrelated aspect changes remain rejected. A different composition still belongs to Rhino's viewport or Named Views. No independent crop/camera controls are introduced.

Allow exports up to 67,108,864 pixels and 16,384 pixels per side. Retain the smaller capture/model-input limits and byte limits. PNG metadata parsing has separate capture and export bounds; increasing export size must not admit larger uploads. The panel shows the generated content area when export resizing occurs. Enlargement adds no generated detail, and a 4× export can still exceed the export limit.

Comparison validates the original capture and result hashes first, then fits the frozen source into the same export frame using the same transform. It never recaptures Rhino. Automatic saving and full-resolution inspection accept the bounded export dimensions.

The diagnostic capture-test service remains unchanged: it receives and returns the original PNG byte for byte. Its client verifies that response before resizing a local export, records both hashes and retains the original native bytes. Resized reference exports must not be described as byte-identical final files or as generated images. Reconnection repeats the identical original wire request.

Validation covers linked typing, invalid input recovery, 2× capture preview, 4× limits, uniform authored landmarks, aligned comparison and an export larger than the capture limit. No new provider, model download or paid generation is required for these checks.

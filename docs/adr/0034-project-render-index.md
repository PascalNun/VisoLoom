# ADR 0034 — The Rhino file carries a render index, not images

Status: Decided by the owner on 2026-11-24. A project reopens with its renders visible.

## Decision

Every completed render that saved a PNG file appends one bounded row to the Rhino file — `VisoLoom\RenderIndexV1`, a document string beside the model pin. The row carries the result id, the saved file's absolute path, the model, view name, display style, final frame size, completion time and whether it was a capture test; at most twelve rows survive. The image bytes never enter the Rhino file: the panel reads a row's pixels from the file on disk when the row is selected, and shows the row's state honestly — a missing or moved file is named, not hidden.

## Rationale

The owner asked whether cloud renders should be visible when a project reopens. Embedding PNGs in the 3dm was rejected immediately: it bloats files and fights every sync the project itself rides on. Nothing changes in how images persist — every render already saves to disk automatically. The render index only reconnects them to the project that produced them, the same way the model pin does, so "Recent renders" describes this Rhino file's work rather than just this session's.

## Consequences

`ProjectRenderIndex` reads and appends with the pin's exact storage pattern: WireJson, length-bounded, field-validated parse, set-and-verify write, `doc.Modified` on append; a read-only or vanished document skips the row and loses nothing but reopen visibility. `RememberResult` appends after a completed save, so a render without a saved file has nothing to reference and no row is written. The panel merges this session's renders with the project's rows (newest first, session first, never duplicated by id): a project row previews, opens for inspection and exports a copy from its own file; Compare stays a session feature because the Rhino file deliberately keeps no source captures.

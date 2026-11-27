# ADR 0034 — The Rhino file carries the project render index

Status: Decided 2026-11-24, revised 2026-11-27 for full project history.

## Decision

Record one bounded Rhino document user-text entry for each successfully saved output. Use the section `VisoLoom.RenderHistoryV2` and the result ID as the entry name. Each entry records the saved PNG location, model, view, display style, output size, completion time and whether it was a capture test. Saving the Rhino file persists the references. The PNG files remain on disk.

Read the previous `VisoLoom\RenderIndexV1` records alongside the individual entries. Deduplicate by result ID and use the newer format when both contain the same render. The former index retained twelve references. Result files that had already fallen out of that list are not automatically associated with the project. A read-only or closed document can still leave the saved image on disk.

## Consequences

The dock presents up to three recent thumbnails and opens a scrollable project list with pages of 50 records. PNGs are loaded only for recent thumbnails or a selected result. A missing or moved file retains its list entry and shows a clear message. Compare uses the source capture when it is still available in the session. Cross-machine access to the PNG requires the file to be reachable at the saved location.

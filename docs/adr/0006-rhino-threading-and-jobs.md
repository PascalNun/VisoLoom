# ADR 0006 — Capture on host thread, detach jobs

Status: Accepted by the user on 2026-09-19.

## Context

Rhino document/display access and UI controls have host thread/lifetime constraints. Inference is slow; panels and documents can close while it runs. Parallel passes from mutable views can describe different scenes.

## Decision

Use the Rhino UI thread for document access and capture. Bind document and viewport at submission; capture sequentially and detect inconsistent state. Background work receives detached values and owned files/bytes only. Marshal result updates back to UI and check panel/document lifetime. Keep jobs outside panel instances.

Bound capture concurrency and memory; first UI allows one active submission. Separate cancellation requests, confirmed cancellation, connection loss and server status. Capture can briefly block; measure it and avoid pretending async wrapping eliminates that stall.

## Alternatives and consequences

Putting all work in `Task.Run` risks unsafe host access. Keeping network work on the UI thread blocks modelling. Duplicating the full document for every click is expensive and still needs display validation.

Requires lifecycle, failure and cancellation tests. Revisit capture isolation only if measured UI stalls demand it and a proven Rhino-supported approach exists. Evidence: [host APIs](../development/rhino-feasibility.md); behavior: [jobs](../architecture/domain-model.md).

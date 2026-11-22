# ADR 0033 — Downloads run with the Rhino session

Status: Decided by the owner on 2026-11-23. Reworks when a model download stops.

## Decision

A model download belongs to the Rhino session, not to the Settings window that started it. Closing Settings mid-download leaves the download running; reopening Settings attaches to it again and shows its progress and Cancel control. At most one download runs at a time. Cancelling it — clicking Cancel, or Rhino quitting — walks the installer's graceful stop, and the staging is removed, so no partial files survive any exit path.

## Rationale

Downloads reach several gigabytes and take minutes; a user starting one and returning to modelling should not pay for opening a settings surface. The size-and-destination confirmation stays in the Settings UI that owns the click — it is a question about intent, not part of the download.

## Consequences

A session-scoped `ModelDownloadService` owns the single running download: its progress state, its cancel, its outcome message, and its stop at Rhino shutdown. Settings surfaces render the service's state and re-attach when opened; the row for the downloading package carries its Cancel control. Folder-changing and file-deleting actions refuse while a download runs, because the running download owns the destination directory it was given.

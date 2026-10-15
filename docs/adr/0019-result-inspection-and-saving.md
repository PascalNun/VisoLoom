# ADR 0019 — Completed-image inspection and automatic saving

Status: User direction confirmed on 2026-10-15. Implemented with automated checks and a source-linked native viewer fixture; the registered updated panel still needs a normal Rhino restart and end-to-end check.

## Decision

Use one ordinary Render workflow for local and cloud providers. The user's later interview answer drops the earlier draft/final concept. A smaller generated image does not establish the appearance of a subsequent larger generation. Keep local Preview capture distinct: it inspects the exact prepared input, and its existing review/submission guard is unchanged. No new model request, quality stage, refinement capability or automatic retry is introduced.

Open completed results in one large source/result viewer, with a quick toggle, shared zoom and pan, Fit and actual display-pixel 100%. The original unpadded Rhino capture and the restored result must match scene identity, scene digest, image dimensions, byte counts and hashes. The viewer owns the validated encoded pair and its decoded images; a panel refresh cannot replace or dispose them. It never recaptures Rhino or restores a view. Prepared input inspection uses the same viewport controls, with its padding disclosed separately.

## Saved outputs and private history

Each completed PNG is saved automatically under the user's local VisoLoom `renders` folder. These PNGs are retained without automatic image cleanup. They are separate from the existing bounded scene/prompt/native-output history. Before deleting an old completed history directory, preserve its PNG in the durable store; if preservation fails or the record is incomplete/damaged, keep the directory. Incomplete history may therefore accumulate until a future explicit cleanup flow is implemented.

Image saving settings optionally select an existing additional folder. It receives only a PNG, with a generated timestamp/result-identity filename, not scene records, source images, prompts or credentials. The output-folder setting is captured when a job starts. Changing it affects future jobs and does not relocate images or modify an accepted job. A missing folder/drive is reported and never recreated as a substitute mount. Save PNG… remains the deliberate one-off export.

Writes flush a temporary file, then rename without overwriting a different file. Repeated recovery of identical completed content is idempotent. The private archive rejects linked parents; selected folders reject a reparse-point root. These checks are not a hostile-filesystem sandbox. Windows uses inherited ACLs; real profile/share acceptance remains pending.

Saving failures do not discard an accepted result or cause generation to run again. Attempt the automatic copy, optional additional PNG and private history independently. Publish the returned image to the panel with explicit warnings and manual-save guidance when no durable image copy succeeded. This is not an OS-crash guarantee between receiving an API response and completing the first write, nor an automatic external backup.

## Evidence and remaining gates

Tests cover display-pixel zoom, aspect/complete-image fit, fixed transforms through toggling, zoom anchors, bounded pan, screen/size changes, frozen pair ownership, wrong-source/hash/frame rejection, durable/idempotent/concurrent PNG saving, unavailable folders, independent copy failure, and preservation before history eviction.

A separate assembly links the current viewer source into Rhino with authored blue/orange images, without replacing the loaded plug-in. The first pass found a shared-layout-column bug; a separate toolbar fixed it. The corrected fixture passed source/result display, button/keyboard toggles, 100% on a 2× display, 125% zoom and Fit. Its private report confirms unchanged document/view state. This is source-level native evidence, not a full installed-panel acceptance or Windows UI test. The control connection stalled after dialog close, as in earlier native checks; the script still completed and recorded its unchanged-state check.

The guided catalogue remains planned as a few curated choices with strengths, hardware fit, download size and local/cloud location. No installer, new model integration, cloud request or runtime dependency is added by this decision. No LICENSE file is created.

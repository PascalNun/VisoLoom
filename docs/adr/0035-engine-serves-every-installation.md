# ADR 0035 — One engine session serves every installed local model

Status: Decided and implemented on 2026-11-25, after the owner's download of FLUX 3 Image Klein never reached the Model menu.

## Decision

The managed engine serves every installed model, not one. The engine configuration lists the machine's installations (bounded set); the engine validates each at session start — a failing check skips that installation without disabling the others — and advertises every validated model. A render's own submission names the model it wants (`JobSubmission.ModelId`, in the protocol since the beginning), and the engine resolves the installation for that model per job, exactly as the per-job worker already loads weights per job. The Model menu lists each installed local model the engine validated, and the session keeps a per-model selection exactly like the cloud providers'.

## Rationale

The owner downloaded FLUX 3 Image Klein; it verified and showed Installed, but the menu never offered it. The prototype engine configuration pinned a single installation from before the download service existed, and the engine's active-model contract accepted exactly one advertised model — two installed models would have collapsed to none. Neither constraint reflected the architecture underneath: the engine worker already loads weights per job, so serving several installations costs nothing between renders.

## Consequences

`connection.json`'s shape is the installations list (`python`, `script`, `installations` of installation receipt + model directory pairs, machine-local like before). The server-side engine wrapper advertises the validated list and resolves per submission; a job naming an unvalidated model fails closed with `modelUnavailable`. The client-side single-active helper became a list contract (`ActiveManagedModels`), and the Model menu gains one row per installed model. The owner's wider request — the Model menu separating local and online models more visibly — is the open UX follow-on on the panel redesign, recorded here so it is not lost.

Refined the same evening, after the owner's restart still showed no FLUX 3: the original gate advertised only installations whose receipt already recorded `nativeEditPassed`, and the acceptance is written by the engine worker itself on a completed edit — a bootstrap deadlock, since the model could never be rendered to earn it (FLUX 2 had earned its acceptance in the prototype days; FLUX 3 never could). The gate now belongs to the honest status, not availability: every available installation (pinned runner, complete verified files) is advertised, the first render is the validation, and Settings' readiness report keeps naming the unvalidated state until the receipt records it.

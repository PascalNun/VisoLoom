# ADR 0018 — Prepared local models and complete-frame sampling

Status: Implemented as an experimental Mac connection on 2026-10-15. Real engine/service evidence is separate from loaded Rhino UI and drawing acceptance.

## Decision

Prepare the selected local weights once, outside Rhino, using the already reviewed MFLUX runtime. Save an independent 4-bit copy and retain its original model card. Neither preparation nor discovery downloads models or installs packages. This does not clear a production runtime installer or redistribution; [dependency terms](../legal/local-engine-runtime.md) still apply.

A private receipt records the complete file inventory, hashes and filesystem identities. Rendering checks identities instead of reading all weights twice to recompute hashes. Replacement, extra/missing files or changed size/modification/change timestamps invalidate the receipt. Windows uses its native change timestamp, not Python's creation-time ctime. Explicit verification hashes everything again and clears prior native acceptance. Controller and worker check the receipt, including after inference. This is a local integrity cache under the user's control, not publisher authentication or an adversarial filesystem sandbox.

The service advertises a prepared model only after runtime checks and a successful real edit for that backend profile. Model versions bind adapter identity and model digest; a changed version requires checking Settings and refreshing Preview. A lock shared by that installation excludes preparation and inference from other job roots. Heavy inference stays in a credential-free isolated Python process.

Use the existing authenticated loopback job service and automatic per-user token handoff. A private local connection file chooses the known Python executable, engine script, installation receipt and model directory. Network submissions cannot supply commands, executable paths, model directories or workflows. Office connections still need authenticated HTTPS and remain separate work. No new unauthenticated listener is added.

The local prompt contains the preset, preservation objective once, vegetation policy and original user instructions. It omits camera/material JSON. The 2048-byte preflight and actual 512-token formatted-prompt limit reject overflow without truncating authorship. The model consumes only the prepared reference image and effective prompt; scene records remain private provenance.

## Complete frame at a smaller resolution

Local canvases use a 64-pixel grid and at most 1,048,576 pixels. Those dimensions avoid MFLUX's internal reference downscale/residual crop. Capture and saved export dimensions remain independent of native model resolution.

The original integer-aspect planner cannot fit some relatively prime viewport dimensions into a small canvas. For models that explicitly allow it, add an optional `ResolutionPlan.Sampling` rectangle with subpixel bounds and **one uniform scale**. Its integer `ContentRectangle` is the covering pixel box; image sampling and coordinate mapping use the fractional rectangle. No source edge is cropped and horizontal/vertical scales remain equal. This changes raster sampling, never Rhino's camera or geometry. The exact sampling bounds are recorded, including resampling for export. An upscaled export is not native 2K generation.

Other models retain the original whole-pixel planner and drawing overload. In particular OpenAI's framing, prompts, disclosure and credentials are unchanged. Pure framing tests check complete bounds, equal scale, coordinate round trips and JSON persistence. The new floating-point drawing path still requires a real Rhino-host check; a standalone Mac process does not initialise Rhino's drawing bindings.

## Jobs and acceptance

Preview freezes the submitted model PNG and effective prompt. The service validates model/source/output hashes before returning a generated result. Existing durable submission identity prevents duplicate accepted requests from running inference again. Cancellation remains requested/running until the worker has exited; waiting without confirmation is reported honestly. Normal terminal-job retention also removes its finished Python copy. A still-running orphan is never deleted underneath a live worker; hard-crash supervision and comprehensive orphan cleanup remain open.

The prepared 4.62 GB model completes a 512 × 448 job in 27.7 seconds, including roughly 2 seconds of model loading. The authenticated service path completes in about 31 seconds; recovery of the same job takes about 22 ms. Real cancellation also passed. A separate 1152 × 896 engine benchmark takes 107.2 seconds and reports 9.72 GiB MLX peak allocation. Its input preparation used an independent affine sampler, not Rhino; it does not establish Rhino drawing acceptance. These measurements describe this M1 Pro/16 GB Mac, not supported performance across hardware. Details and material colours still drift.

The Settings/model-menu source is implemented, but the Mac was locked during the native check. [The in-host framing script](../../tests/native/check-local-framing.py) uses a synthetic image without opening a fixture or changing the original document. Loaded panel interaction, memory pressure alongside large Rhino files, Windows GPU inference and distribution review remain acceptance gates. See the [current review](../development/phase-4-review.md).

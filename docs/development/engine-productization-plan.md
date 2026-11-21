# Local engine productization implementation plan

Written 2026-11-21 as the execution plan for [Track 7](onboarding.md#track-7--local-engine-productization) of the onboarding inventory. [ADR 0022](../adr/0022-engine-bundling-and-one-click-model-install.md) remains the decision record for the packaging direction, the catalogue contents and the memory guidance; this plan sequences its remaining gates into implementable milestones without changing any decision. Evidence language follows the repository rules: unless a dated note says otherwise, everything here is **planned** and no measurement is cited before it has been recorded.

**Update, 2026-11-21 (later the same day):** Milestone A's engine side is implemented — `engine/model_install.py` downloads, verifies and registers reviewed packages through the existing receipts (12 new tests; engine suite 77 passing + 1 skip); the hosting findings below carry the encoder-pin correction recorded that day. Settings wiring, the runner profile and the remaining Milestone A items stay open. **Same day's additions:** the Settings Review-panel wiring is implemented; the native FLUX.2 klein 4B runner profile exists with per-platform pins (`SdcppFlux2Backend`), and Milestone C's arm64 Metal runner passed both frozen edits on M1 Pro — engine suite 91 passing + 1 skip, all records below and in ADR 0022. Milestone B (Windows CUDA build and acceptance) remains open on the NVIDIA machine.

## What exists today

- Two adapters under the `engine.py` contract, each with recorded development-acceptance evidence: `mflux_backend.py` (Apple Silicon, FLUX.2 klein 4B through MFLUX; prepared 4-bit copy 4,619,709,935 bytes; 512 × 448 edit in 27.7 s, 1152 × 896 in 107.2 s at 9.72 GiB MLX peak) and `sdcpp_backend.py` (x86_64 Linux, FLUX.3 Image Klein Q4_K; full 512 × 448 edit ~98 min on the floor CPU; four-hour budget; cancellation/deadline/budget enforced as one POSIX process-group kill, with a regression test).
- Verification machinery: `model_store.py` receipts binding file inventories, SHA256 hashes and filesystem identity (the Windows `ChangeTime` path is implemented), one profile per adapter, and first-edit acceptance gating advertisement.
- Folder registration: `model_library.py` bounded inspection, the 32-entry private list, the `plan` setup-step reports, and the C# bridge behind **Find existing model…** with no HTTP endpoint.
- Settings surfaces: catalogue rows, folder inventory, "Check setup", the connections view — but **no downloader, no bundled runner, no queue, no supervisor**, and the engine performs no network access today.

## Product definition

The shipped bar, from ADR 0022's consequences: on a clean machine, a designer installs one Rhino package, opens Settings, downloads one reviewed model package (or registers an existing folder), and completes one real edit in the ordinary Render flow. The per-install development gate is replaced by runtime checks — receipt, runner identity, capability report and first-edit acceptance for the exact backend profile. Hardware guidance carries measured budgets per tier, and nothing in that path is labelled experimental. [ADR 0022](../adr/0022-engine-bundling-and-one-click-model-install.md#consequences) keeps model weights as separate verified downloads from reviewed sources.

## Milestones

| Milestone | Completion gate | Can start |
| --- | --- | --- |
| **A — One-click install** | The reviewed FLUX.2 klein 4B ggml package installs end to end: download, staging, hash verification, receipt registration, activation attempt with honest status | Immediately; no runner build required |
| **B — Windows CUDA runner** | First real edit on Windows x86-64 with NVIDIA CUDA under the unchanged engine contract | In parallel with A; needs the NVIDIA machine (Gate 3) |
| **C — Apple Metal decision** | Measured comparison of the sdcpp Metal runner against the MFLUX/MLX route on the same prepared edits | After the B build recipe exists |
| **D — Managed lifecycle** | Automatic start on first local render, retention/eviction behaviour, orphan and result cleanup | Last; needs B/C evidence for the budgets it enforces |

Native Rhino acceptance (Track 1) runs alongside every milestone; each one adds Settings interactions that need the in-host walkthrough.

Direction, recorded 21 November 2026: the bundled reviewed runner inside the plug-in is the product path, and the prepared MFLUX runtime stays a developer route — not the destination. Breadth of model families on one runner is a standing criterion of the runner choice: the ggml runner covers the FLUX family first, then Qwen-Image, LongCat and SD-class models, exactly as the catalogue requires. Runner work starts in parallel with the Settings increment rather than waiting for milestone hardware: the arm64 Metal build on the development Mac is the first step, and Windows CUDA follows on the NVIDIA machine.

## Milestone A — one-click model install

### Catalogue entry format

Catalogue data stays compiled into the plug-in as it is today; no network supplies model metadata. A local install entry adds: id and display name, purpose text, hardware-tier label, licence terms (name plus publisher link), total bytes, the per-file list of pinned download URLs with exact byte sizes and SHA256 hashes, and the reviewed runner recipe it installs toward. The first entry is the ADR 0022 fixture: **5,207,177,908 bytes** across three Apache-2.0 files — the leejet FLUX.2 klein 4B Q4_0 transformer, the Qwen3 4B Q4_K_M encoder, and the BFL full encoder with small decoder — whose published sizes were confirmed by anonymous header requests in [ADR 0022](../adr/0022-engine-bundling-and-one-click-model-install.md#local-download-catalogue). A new runner profile (`sdcpp-<commit>-flux2-klein-4b`, recipe from the runner's FLUX.2 guide) owns the conditioning contract; it is defined here but only becomes live where a platform runner exists.

### Downloader

`model_install.py` on the engine side, reached through the existing desktop bridge; no new HTTP endpoint and no network inputs become executable configuration.

- **Destination discipline.** Per-user default model folder, full path shown in Settings, changeable before install; an external drive can be selected first. Changing the default affects future downloads only; registered folders keep their locations, per ADR 0022.
- **Staging and verification.** Files download into a staging directory on the destination volume, each verified against its pinned size and SHA256 before an atomic move into the managed folder. A partial or mismatched file never exists in the final location, is never registered, and is never advertised — the engine README already forbids advertising partial downloads. Free space (total bytes plus working slack) is checked before the first byte transfers.
- **Honest failure and recovery.** Cancellation and interruption are safe and repeatable; an interrupted install leaves every existing working model untouched (ADR 0022's rule). Hash mismatch or truncated transfer deletes the staged file and names the next action. Byte-range resume is optional and second; a clean restart satisfies the same guarantee.
- **Registration and activation.** A completed install registers through the existing receipt format — no new verification concept — then follows today's readiness path: runner identity, capability report, first-edit acceptance. Where no runner serves the profile on that machine, the entry shows a true "installed, awaiting a runner for this computer" state instead of readiness.
- **Network boundary.** HTTPS `GET` to the pinned hosts only; the pinned-host list is asserted by a test, mirroring the origin checks in the service. Nothing else the engine does today gains network access.

### Settings UI

**Install model…** appears on catalogue rows where the package is reviewable for that platform; per ADR 0022 a user may still download a package whose runner exceeds the current machine, with the tier label carried honestly. The action shows purpose, licence terms, total size and required free space before starting, then progress with pause/cancel. **Forget location** and **Delete downloaded files…** stay separate actions with the ADR 0022 wording (deletion only for VisoLoom-managed downloads, named folder, explicit confirmation); **Locate model…** or reverification covers moved or changed files.

### Settings wiring — the next increment

Implemented 21 November 2026 — [review record](phase-4-review.md#settings-wiring--reviewed-downloads-in-the-panel--2026-11-21); step 4, the hands-on walkthrough on Mac and Windows including the interrupted-network check, remains the gate.

The concrete slice that finishes Milestone A, sequenced so a fresh session can execute it without re-deriving:

1. **Bridge surface.** The desktop bridge gains the fixed `model_install.py` beside its existing `model_library.py` surface: bounded `list` and `plan` invocations reuse the existing 15-second pattern; `install` is a background process whose incremental JSON output the bridge polls — one object per completed file plus the final record — and whose cancellation terminates the process (staging cleanup is already engine-owned).
2. **Panel and states.** The Models page gains **Install model…** from data the bridge returns: display name, purpose, licence terms with publisher links, total bytes and the space check from `plan` before the first byte. Progress shows per-file completion; failures carry the engine's honest reason. Destination follows ADR 0022's storage rules (per-user default shown, changeable before install). A finished install appears under Local models with the truthful status **installed, awaiting a runner for this computer** until the runner profile exists; **Forget location**/**Delete downloaded files…** arrive with the ADR 0022 wording in the same increment.
3. **Tests.** Bridge unit tests around bounded invocation, unknown-configuration refusal and progress parsing against fixture output; the engine suite already covers the download semantics end to end. No provider or protocol change.
4. **Native acceptance (hands-on in Rhino).** The install walkthrough on Mac and Windows — including a deliberately interrupted network connection to verify the honest recovery wording and a full-screen/narrow page check.

### Hosting and entitlement findings

Recorded so the catalogue needs no third-party account, the property ADR 0022 requires:

- The fixture's transformer and decoder are ungated and Apache-2.0, with anonymous `HEAD`/`GET` access (sizes confirmed in ADR 0022; hashes captured 2026-11-21 as the `x-linked-etag` SHA-256 of each pinned source). The BFL model card re-confirmed 2026-11-21: FLUX.2 klein 4B is "fully open" under Apache 2.0, with an upstream ~13 GB VRAM guidance for the RTX 3090/4070 class; the 9B release is non-commercial. [Model](https://huggingface.co/black-forest-labs/FLUX.2-klein-4B)
- **Encoder-pin correction, 2026-11-21:** the runner author's Qwen3-4B mirror (`leejet/Qwen3-4B-GGUF` at commit `bc640142`) now refuses anonymous resolve with 401 on both `HEAD` and ranged `GET`. The official `Qwen/Qwen3-4B-GGUF` repository serves the identical file — same commit, same linked size and etag — anonymously, so the encoder pin moved to the revision-pinned official path. The ADR 0022 requirement to exercise editing with the exact recorded combination stands unchanged; the first real review of that combination happens at the runner's first-edit acceptance.
- FLUX.3 Image Klein is **gated**: Hugging Face requires an account and acceptance of the FLUX Kommunity License v1.0 and its Acceptable Use Policy before file access (checked 2026-11-21). No one-click download exists for it; the catalogue publishes it as publisher-links-plus-register, exactly ADR 0022's treatment, and it stays that way unless redistribution terms are ever obtained. [Model](https://huggingface.co/black-forest-labs/flux-3-image-klein)
- stable-diffusion.cpp is MIT, and upstream documents CPU (AVX variants), CUDA, Vulkan and Metal builds — the per-platform runner strategy rests on documented support, with the pinned-commit build instructions recorded per platform in Milestones B/C. [Repository](https://github.com/leejet/stable-diffusion.cpp) · [Build guide](https://github.com/leejet/stable-diffusion.cpp/blob/master/docs/build.md)

## Milestone B — Windows CUDA runner

- **Build recipe.** Pinned stable-diffusion.cpp commit (the reviewed runner line is 80c4707) with its patched ggml submodule, CMake `SD_CUDA=ON` for x86-64, CUTLASS submodule initialised; MSVC and CUDA toolkit versions recorded with the build. A Vulkan build follows as the second artifact, not before CUDA qualification. Every output is hashed and named in the runner inventory the receipts bind to.
- **Vendoring.** The reviewed runner ships inside the plug-in folder under `engine/windows-x86_64/` — the same platform tag the engine's runner staging uses, with Windows' `AMD64` vendor name normalised away — with development-copy layout parity, no PATH dependence, and a fail-closed identity check at startup; the existing unknown-profile rule covers runner/recipe mismatches. An artifact-level licence and notice review — sdcpp/ggml (MIT), CUDA and MSVC redistributables under their own terms — extends [third-party dependencies](../legal/third-party-dependencies.md) before anything ships.
- **Contract and cancellation.** The engine's process-group semantics become a Windows job object covering the whole runner tree, the known prerequisite the engine README names for claiming cancellation support; the descendant-kill regression test mirrors the POSIX one. Windows state continues to inherit user-profile ACLs, and the planned native ACL validation covers the engine's private folders.
- **First-edit acceptance.** One representative end-to-end edit on real NVIDIA hardware through the full service contract (gate below), recording cold and warm time, peak RSS and VRAM on a production-representative canvas within the 1,048,576-pixel grid. Before this acceptance, the Windows runner exists in the inventory but no Settings entry claims it works.

## Milestone C — Apple Metal decision

Build sdcpp for arm64 with the same pinning and receipt rules, then run the frozen edits the MFLUX route already measured (512 × 448 acceptance input; the 1152 × 896 benchmark) on the same machine and record both. ADR 0022's rule decides the outcome: the bundled native runner becomes the product default once its backend profile passes first-edit acceptance, and MFLUX/MLX stays the developer-grade bring-your-own path for development and comparisons. No Python stack ships for Apple either; the [Mac development lock](../../engine/requirements-mac-arm64-py314.lock) remains a development artifact.

**Outcome, 2026-11-21:** the arm64 Metal runner is built from the same pin (`-DSD_METAL=ON`, Metal shaders embedded), staged under `engine-prototype/bin/darwin-arm64/sd-cli` and pinned by SHA-256; `SdcppFlux2Backend` serves the profile, and both frozen edits passed on M1 Pro: the 512 × 448 first-edit acceptance in 63.4 s and the 1152 × 896 production-frame benchmark in 212.6 s, with the peak Metal compute buffer at 4.9 GB during the decode phase and the staged weights held until after it (ADR 0022's record). MFLUX keeps its measured lead on both frames (27.7 s and 107.2 s respectively) — all four numbers are interactive-fast at 4 steps, and the native route needs no Python stack. Replacing this tier's blanket four-hour bound with the measured budget stays sequenced release work, together with memory-headroom evaluation alongside a large Rhino model.

## Milestone D — managed lifecycle

- **Automatic start.** The first local render starts the runner under the user's account with "Starting local model…" visible; the load is cancellable, and startup failure keeps the prepared reference and names the next action, matching ADR 0022's wording for the prepared-capture path.
- **Concurrency.** One running local edit stays the v1 contract; a second submission keeps the honest `engineBusy` reason in the panel. A shared queue and persistent supervisor are deliberately not v1 (single-designer desktop product) and are re-evaluated with the office-server pilot ADR 0022 already plans for.
- **Memory and idle retention.** Idle weight retention is only added where B/C measurements show a meaningful speed gain with headroom, and any retained weights are evicted on model change or memory pressure, per ADR 0022's memory-while-modelling section. Orphan and terminal-job retention policy, plus the sampled budget refresh against the measured per-tier peaks, complete the cleanup work the engine README lists as open.

## Hardware tiers and measured budgets

| Tier | Definition | Status today |
| --- | --- | --- |
| CPU floor | x86-64 AVX2 CPU, no usable accelerated GPU | Documented by the FLUX.3 spike: ~350 s/step, ~98 min per edit — real but hour-scale; presented as such |
| Workstation GPU | Windows x86-64 with NVIDIA CUDA (Vulkan variant after) | Budgets measured in Milestone B |
| Apple unified | Apple Silicon, Metal or MLX | Measured 2026-11-21 (M1 Pro): native runner 63.4 s per 512 × 448 edit and 212.6 s per 1152 × 896 frame; MFLUX 27.7 s and 107.2 s. Buffers and acceptance in ADR 0022 |

The blanket four-hour bound stays until a tier's measured budget replaces it for that tier — the sdcpp profile's own rule. Catalogue entries show package size from the pinned files and tier guidance only from recorded measurements; unmeasured combinations say so. The live headroom check at render time follows ADR 0022's memory-while-modelling guidance and names whether system memory or GPU memory limits the job.

## Release-check fixtures

New engine/protocol fixtures per milestone, joining the engine suite at 91 passing tests plus one skipped network case (2026-11-21):

- **A:** hash mismatch → staged deletion and no registration; interrupted then repeated install → complete package; insufficient free space → refusal before transfer; cancelled download → existing models untouched; staging medium removal/rotation → no misleading ready state; pinned-host assertion on every download; UNC/reparse rejection unchanged.
- **B/C:** unknown runner builds fail closed (native per-platform gate: no pin, no probe; `runtimeMismatch` on digest change); per-platform pin staging paths and profile selection covered. Remaining: the Windows job-object descendant-kill regression; receipt/runner mismatch on Windows ACL inheritance.
- **D:** idle retention only when a recorded measurement justifies it; second submission shows the honest busy reason in Settings and the panel.

## Open decisions

1. **First shipping catalogue composition** — FLUX.2 klein 4B is ADR 0022's recommended starting model; LongCat needs its autoencoder qualification before it can appear (ADR 0022's note).
2. **CPU floor tier placement** — this plan proposes documented-only rather than a downloadable recommendation; the decision stays open.
3. **Hardware access for B4 and C** — a real NVIDIA machine for the Windows first edit, and which Macs measure the Metal decision.
4. **Hosting** — direct pinned-Hugging-Face downloads are the no-account default; any mirror or proxy adds a terms decision before use.
5. **VisoLoom's own licence** (Track 10) before any public package; Yak upload steps follow [ADR 0022](../adr/0022-engine-bundling-and-one-click-model-install.md#proposed-release-sequence).
6. **Gated models** — publisher-gated weights (FLUX.3 Image Klein today) stay register-only permanently unless redistribution terms are ever obtained; no account flows are added to enable them.

## What this plan does not decide

Architectural fidelity comparisons ([Track 2](onboarding.md#track-2--capture-fidelity-and-context-refinement)); the FLUX.3 caption-preparation path ([FLUX.3 review](../providers/flux3-klein.md)); the signed remote catalogue update channel for cloud models ([model-selection plan](model-selection-plan.md)); the office-server pilot (ADR 0022's server sections keep the direction; a separate plan follows when Milestone D lands); Library assets; and the software licence. None of those blocks A–D above.

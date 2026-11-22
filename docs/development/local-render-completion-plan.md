# Local render-loop completion plan

Written 2026-11-23 as the execution plan for the local-engine remainder the owner confirmed this day: make every download card render, give every render honest feedback, expose the sampling schedule as a bounded user setting, offer the native model when a cloud render fails, curate the More tab behind the recorded gates, and start Track 9's extraction prerequisite. [ADR 0022](../adr/0022-engine-bundling-and-one-click-model-install.md) keeps the packaging direction; the [engine productization plan](engine-productization-plan.md) sequences the platform milestones this plan feeds. Evidence language follows the repository rules: unless a dated note says otherwise, everything here is **planned**; a recipe is only *accepted* after a real edit passed on the named hardware.

## M1 — One reviewed runner recipe per download card

Every package in the installer manifest gets a native engine profile on the same pinned per-platform runner build (Metal accepted on Apple Silicon, CUDA pending on Windows, Linux as the floor tier). Recipes are the runner's own model documentation, frozen as generation identity:

| Card | Package | Recipe (steps, cfg, extras) |
| --- | --- | --- |
| FLUX 3 Image Klein | flux3-image-klein-gguf | 16, cfg 1.0, euler; Qwen3-VL 4B encoder, FLUX.2-family VAE |
| FLUX 3 Image Klein Base | flux3-image-klein-base-gguf | 20, cfg 3.5, euler; same components |
| FLUX.2 klein 9B | flux2-klein-9b-gguf | 4, cfg 1.0, euler; Qwen3-8B encoder, FLUX.2 VAE |
| Qwen-Image-Edit-2511 | qwen-image-edit-2511-gguf | 20, cfg 2.5, euler, flow-shift 3, zero-cond-t model arg; Qwen2.5-VL 7B encoder with its mmproj vision file |
| LongCat-Image-Edit | longcat-image-edit-gguf | the turbo schedule recorded in the runner docs / model card |
| FLUX.2 dev | flux2-dev-gguf | 20, cfg 1.0, euler; Mistral 3.2 24B encoder, FLUX.2 VAE |

- Each profile names its files through the installer's own pins; the adapter and the catalogue keep describing the same reviewed files.
- Acceptance stays per installation receipt: a new profile reports *unvalidated* until a real edit passed — the engine check keeps saying so in Settings.
- The catalogue cards replace their "recipe still to ship" sentence with this honest state.

## M2 — Sample-steps override, recorded in provenance

The sampling schedule becomes a bounded per-render setting: default is the reviewed recipe, an override is clamped per profile and recorded in the job record alongside the recipe's other generation-identity fields. The override travels with the render request (intent), never as hidden engine state, so a result always says what produced it. The distilled models keep their guidance honestly: fewer steps are faster; more rarely help.

## M3 — Honest feedback for every render

- **Local**: the status line keeps phases, step and ETA, and the preview area gains a real progress bar driven by the sampler, plus the live preview image the runner writes during sampling (decoded with the model's own VAE, refreshed at bounded intervals, shown when present and readable, never faked).
- **Cloud**: the current endpoints are single synchronous calls with no honest percentage; the panel shows each phase, a moving indeterminate bar and a running clock. BFL's documented queue-position/percent polling is recorded here for the day its reference decision is made.

## M4 — The native fallback offer

When a cloud render fails and a verified local model exists on this computer, the status line offers the local render by name in the same message — the Model menu stays the one deliberate step, nothing switches or spends another provider on its own.

## M5 — More-tab curation behind the three gates

Candidates are judged by the recorded gates: the pinned runner supports the architecture, the licence is clear for the user's role, and the model genuinely helps architecture renders. Research pass over the runner's supported set (Z-Image family, Qwen Image 2.1, LLaDA package conversion, and the rest of the runner's supported list); only what clears all three gates becomes a card — a gated or unsupported candidate stays a research note, never a download row.

## M6 — Track 9 P1: asset-aware extraction (start)

[The asset production plan](library-asset-production-plan.md) owns the direction. This plan starts its P1: nested block instances resolve to their definitions and transforms inside the existing extraction bounds and privacy rules, and the first verification fixture pins how definition metadata survives copies, edits and save/reopen before any UI depends on it.

## Validation

M1/M2 extend the engine suite (recipes as runner commands, bounds, package/file mapping) and the Core/Protocol suites (catalogue/profile mirroring, additive protocol fields); M3/M4 get panel tests where the logic is testable headlessly; every commit keeps the suite green and the build warning-free. Real first-edit acceptance follows the hardware: the owner's Mac for Metal profiles as their downloads complete, the NVIDIA machine for CUDA.

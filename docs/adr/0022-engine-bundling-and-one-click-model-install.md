# ADR 0022 — Bundled engine infrastructure and one-click model installation

Status: Packaging direction confirmed by the user on 2026-11-08: the plug-in ships the local-engine infrastructure, and installing a model is a single explicit, verified download. Backend selection, per-platform runtimes and the first real edit on non-Apple hardware remain gates; this record decides which parts install together and which are first-use downloads, because folder layout, platform selection and the Settings install flow shape core code before the second backend exists.

## Decision

The distributed plug-in folder is the installation unit for infrastructure. The .NET assemblies ship as today, and reviewed per-platform native engine runners ship inside the same folder under `engine/<os-arch>/`, selected at runtime by the platform. The development-copy layout and any eventual packaged install keep the same shape, so they differ only in provenance, not in structure.

Model weights are never bundled with the plug-in. Installing a model is one explicit action in Settings: a curated catalogue entry states model purpose, hardware guidance, licence terms and download size; a pinned URL is fetched, content-hashed, prepared, verified and published through the existing receipts and connection flow. A partial or unverified download is never advertised as a model, for the same reasons the prepared-model receipts fail closed today. The pinned source must be reachable by a user who has only consented to VisoLoom's own terms: end-user installation must not require a third-party platform account or a separate access gate, so the hosting and entitlement choice for catalogue sources is part of the packaging/licence review, not an assumption inherited from a development spike.

The catalogue stays curated and edit-quality-first. Entries are added per model after the existing dependency and licence review. The framework is not FLUX-only — ADR 0017 keeps adapters per editing model — but breadth is not the goal, and a new entry must beat the current choice on an axis the panel states.

The Apple-Silicon MLX path remains a developer-grade, bring-your-own prepared runtime, unchanged. The bundled native runner is the intended product default once a backend passes a first-edit acceptance for its backend profile, using the existing acceptance concept; until then the shipped infrastructure is inert for local generation, and the reference-service and OpenAI paths continue to work unchanged.

All vendored runner code passes the dependency and licence review before it ships in any package. Reviewed runtimes and the catalogue's pinned model sources are the only executable and content origins the plug-in introduces. The managed companion's start/stop ownership and updater mechanics stay open implementation decisions; installation and updates of the service travel with the plug-in rather than as separate user installs.

## Backend candidate and gate

The candidate for the bundled universal runner discussed on 2026-11-08 is a ggml-family CPU/Vulkan-class native stack, chosen for the packaging properties this decision needs: a small per-platform executable, mature quantised single-file weights, and execution paths on hardware beyond Apple Silicon, including integrated graphics. The choice is confirmed only by a spike: one real 512 × 448 edit end to end through the engine contract on non-Apple hardware, with measured time and an explicit FLUX.2 klein 4B editing-pipeline maturity check in that stack (unverified as of this record). A failed spike reopens the backend question without reopening the packaging decision.

## Alternatives

Distributing a Python runtime keeps the current Mac pipeline but cannot make first-use setup one click and does not pass a package review as-is. An ONNX-based universal runner trades conversion and operator-coverage effort onto every catalogue entry. Bundling weights bloats the plug-in install and couples plug-in updates to model licensing. A separate engine installer adds a second moving part for the user and duplicates the receipts machinery. Keeping MLX as the only local engine leaves every non-Apple platform without a first-class local path.

## Consequences

The plug-in folder grows by engine-runner megabytes, not gigabytes. The Settings install flow becomes product surface (progress, terms, hardware notice) instead of the current prepared-folder steps, which remain available for development and for reuse of already-verified weights. Runner updates travel with plug-in updates and stay verified at runtime by the existing file-identity checks. Windows and Linux/Wine acceptance gates remain as in ADR 0010's native-testing stance; nothing in this record claims local-generation compatibility beyond platforms with a first-edit acceptance recorded.

## Spike outcome (2026-11-08)

The gate defined above ran on the Linux/Wine development machine: 4-core
AVX2, 8 GB RAM, Intel HD 620 (Kaby Lake) integrated graphics. Runner:
stable-diffusion.cpp master 80c4707, built from source; model package:
the runner author's FLUX.3 Image Klein Q4_K (3.51 GB), Qwen3-VL-4B
Instruct Q4_K_M text encoder (2.50 GB, Apache-2.0) and the FLUX VAE
(336 MB), reference image from Black Forest Labs' own klein assets.

Result: the ADR's maturity gate passes. FLUX.3 klein-family image
editing runs end to end through the ggml stack — the full 512 × 448-pixel
edit with one reference image and a short edit prompt completed in 98
minutes 5 seconds on the CPU backend (16 distilled steps at a steady
~350 s/step, model load 17 s, ~280 CPU-minutes total). The output image
shows the preserve-and-replace edit behaviour the panel's workflow needs.

Backend reality on floor hardware: the Vulkan build configures and loads
correctly, but the HD 620 dies with `device lost` during compute, so the
Vulkan path is not viable on this class of integrated graphics; the CPU
path is the floor tier's only route. Hardware guidance must therefore
distinguish a modern Vulkan-capable GPU tier from a CPU-only floor tier,
and the catalogue's local-install size statement is ~6.4 GB for this
model package.

The packaging assumptions hold: the runner is a small self-contained
executable (both backends built together as binaries measured in
megabytes), weights are single GGUF files, and the whole install ran
without Python. The end-to-end order can proceed to an engine adapter
implementing this backend under the engine.py contract.

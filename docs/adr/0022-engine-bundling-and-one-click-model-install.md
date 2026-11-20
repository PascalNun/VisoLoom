# ADR 0022 — Bundled engine infrastructure and one-click model installation

Status: Packaging direction confirmed by the user on 2026-11-08. The plug-in ships the local-engine infrastructure and models are separate, verified downloads. The 2026-11-20 catalogue direction covers multiple model families and hardware tiers. The product presents supported packages as ordinary downloads. Development evidence and benchmark coverage live in the repository.

## Decision

The distributed plug-in folder is the installation unit for infrastructure. The .NET assemblies ship as today, and reviewed per-platform native engine runners ship inside the same folder under `engine/<os-arch>/`, selected at runtime by the platform. The development-copy layout and any eventual packaged install keep the same shape, so they differ only in provenance, not in structure.

Model weights are installed separately from the plug-in. One download action in Settings fetches the complete package for models with a reviewed direct download, including its encoder and decoder where required. For publisher-gated or separately licensed weights, the catalogue can link to the official source and register an eligible customer's local files. The catalogue shows hardware guidance, licence terms and total download size where known. Verify files against pinned sources and hashes, then register the package through the existing receipt and connection flow. The Render selector lists a model once the verified files and local runtime pass the readiness check.

The catalogue covers several model families and hardware tiers, from compact models to larger workstation options. Each model receives a complete Rhino view and the selected representation through its own recipe. The designer chooses the representation in the Render panel. Follow ADR 0017 for model-specific image conditioning. Review the complete package and the runner recipe before listing it for download. Benchmarks establish recommendations and measured hardware guidance.

The Apple-Silicon MLX path remains a developer-grade, bring-your-own prepared runtime, unchanged. The bundled native runner is the intended product default once a backend passes a first-edit acceptance for its backend profile, using the existing acceptance concept; until then the shipped infrastructure is inert for local generation, and the reference-service and OpenAI paths continue to work unchanged.

All vendored runner code passes the dependency and licence review before it ships in any package. Reviewed runtimes and the catalogue's pinned model sources are the only executable and content origins the plug-in introduces. The managed companion's start/stop ownership and updater mechanics stay open implementation decisions; installation and updates of the service travel with the plug-in rather than as separate user installs.

## Backend candidate and gate

The original runner spike used a ggml-family stack for its small per-platform executable, quantised weights and range of hardware backends. The Linux CPU edit with FLUX.3 Image Klein verified the engine contract. Its timing and the failed integrated-GPU Vulkan run put accelerated backends first for interactive use. Qualify each platform runner with one representative end-to-end edit, starting with FLUX.2 klein 4B. Other supported editor profiles use the same job and installation path. Benchmark representative configurations as hardware becomes available.

## Alternatives

Distributing a Python runtime keeps the current Mac pipeline but cannot make first-use setup one click and does not pass a package review as-is. An ONNX-based universal runner trades conversion and operator-coverage effort onto every catalogue entry. Bundling weights bloats the plug-in install and couples plug-in updates to model licensing. A separate engine installer adds a second moving part for the user and duplicates the receipts machinery. Keeping MLX as the only local engine leaves every non-Apple platform without a first-class local path.

## Proposed release sequence

1. Build a lightweight Rhino package for cloud use and test installation on clean Mac and Windows setups. Settings uses the existing connection check so a designer can supply an API key and start. Rhino's package manager supports separate Mac and Windows distributions. [McNeel package guidance](https://developer.rhino3d.com/en/guides/yak/the-anatomy-of-a-package/)
2. Ship a reviewed local runner and a catalogue with a recommended starting model and workstation options. Qualify the runner with one real edit. Include other downloadable profiles once they have a complete reviewed package and a compatible runner recipe. The plug-in starts the runner as a separate process under the user's account. Validate a clean install, reuse of already installed files, an interrupted download, a moved or disconnected drive, and a plug-in update. Repeat on other platforms as runners qualify.
3. Package a standalone VisoLoom server for the first validated server platform. Test from a second machine using an authored capture. Cover both a home or office network and access through a private VPN, with the same credentials, capabilities and job checks in both cases. Keep the current loopback service as the development and managed-local path.
4. Test a Draw Things connector separately. Start with one exact editing profile and prove that it runs a Rhino reference edit from the same Draw Things weights used by its desktop app. Record how Rhino connects to the Draw Things backend and whether the backend opens the existing model folder directly. Offer the connector after verifying editing, cancellation behavior and the expected data destination.

An offline model pack can use the same verification and activation flow for offices that provision several workstations, subject to redistribution terms. The current prepared-folder development workflow precedes these release steps.

## Model storage and reuse

- Keep weights in a user-level model folder outside the Rhino plug-in. Use a per-user default, show the full path in Settings, and offer **Open model folder** and **Change download location…**. A user can pick an external drive before installing. Changing the default affects future downloads. Existing entries keep their locations until the user chooses to relink them.
- **Install model** shows the exact model variant, compatibility, download size and required free space. Check for a complete compatible package in the selected library before transferring bytes. Stage and verify pinned files, then register the installation with the existing receipt flow. Activate after the local runner confirms the installed revision and its image-edit capabilities. Interrupted downloads and failed checks leave existing working versions available. Verified weights survive plug-in and runner updates.
- **Use existing model folder…** registers a compatible folder in place, with no copy. Compatibility covers the exact variant, weight format, quantisation and required companion files. Read external folders without writing to them. If a model needs conversion, show the additional disk cost before the user chooses to create a compatible copy. If a volume disappears or files change, show **Locate model…** or reverify the changed files.
- Distinguish **Forget location** from **Delete downloaded files…**. Forgetting only removes VisoLoom's registration. Deletion applies to VisoLoom-managed downloads with an explicit confirmation that names the folder and explains that other apps using those files will lose access. Externally managed files remain under their owner's control.

Keep installed models visible under **Local models**. **Browse models…** shows the catalogue across hardware tiers and provides practical fit guidance. Prioritise Apple Silicon and Windows x86-64 as runtime targets, with Windows Arm tracked separately. A user can download a listed model for later use even when its runner or memory requirements exceed the current machine. The Render selector shows installed models with a compatible runtime. Check live memory headroom for the selected canvas at render time.

For the planned managed local flow, start the runner automatically when the first local render needs it and show **Starting local model…** while it loads. VisoLoom owns the small companion that starts and stops inference, checks models and reports job status. Use an existing inference runtime for the model itself. A designer installs one Rhino package, then downloads a model or selects a compatible folder in Settings. The existing development build still needs the Test VisoLoom launcher and a prepared model. A standalone server has its own lifecycle so it can accept jobs with Rhino closed.

Draw Things supports an external model folder and documents using the app's model directory with its own CLI or gRPC server. That is a concrete route to one set of Draw Things weights through a supported connector. VisoLoom's current MFLUX and ggml runners use different package layouts, so a matching model name alone does not establish reuse. Draw Things also documents importing compatible models. Treat import as a separate workflow until the exact variant is tested for format conversion and disk use. [Draw Things model management](https://docs.drawthings.ai/documentation/documentation/2.models/) · [Draw Things CLI and server](https://github.com/drawthingsai/draw-things-community/blob/main/README.md#quick-start-with-draw-things-cli-on-macos) · [VisoLoom engine layouts](../../engine/README.md)

## Local download catalogue

See the [local editor research](../providers/local-image-editors.md) for the wider inventory, the recommended options per representation, and commercial FLUX weight licensing. The catalogue presents up-to-date supported paths to each model's weights. Keep publisher accounts and any paid licence reporting separate from the inference destination.

Show each model's name, available download size, and the kind of computer it suits. Offer the same **Download** or **Use existing files** flow for compact and larger models. Show download progress, installation state and an actionable reason when a particular computer cannot run the model. Keep benchmark coverage, prototype status and engineering notes in the repository.

List packages with reviewed terms, verifiable files and a documented reference-image editing recipe for a bundled runtime. Let designers select an editor once the installed files match and the runtime reports the required capabilities. Do the first edit in the ordinary Render flow. If it fails, keep the prepared capture and explain the next available action. Benchmarks help choose the recommended starting model and refine hardware guidance.

Keep the catalogue data simple. A model revision specifies file sources and hashes, applicable terms, expected hardware fit, and a reference to a reviewed runner recipe. The recipe owns the exact conditioning, scheduler and input limits. A catalogue entry can select an existing recipe. New runtime behavior is part of a plug-in or runner update. Shared install, render, cancellation and result handling work across all model families and sizes.

| Model | Recommendation |
| --- | --- |
| **FLUX.2 klein 4B (distilled)** | First recommended model for sketch and photorealism. The Apache 2.0 editor is our current Apple Silicon development baseline. Use the 5.21 GB candidate below for native-runner qualification. [Black Forest Labs model comparison](https://bfl.ai/models/flux-2-klein) |
| **FLUX 3 Image Klein** | Include as the first newer FLUX option. Use the model's full-caption recipe and compare photorealism, material placement and sketch fidelity. The existing Linux CPU test exercised the edit contract. Guide users to publisher weights and applicable licence terms. [Publisher model](https://bfl.ai/models/flux-3-image-klein) · [VisoLoom review](../providers/flux3-klein.md) |
| **FLUX.2 klein 9B and FLUX.2 dev** | Larger FLUX options for eligible workstations and studios. Treat 9B as the first larger comparison and dev as the higher-memory option. Users obtain compatible weights under the applicable terms. [BFL licence tiers](https://bfl.ai/open-weights) · [Runner guide](https://github.com/leejet/stable-diffusion.cpp/blob/master/docs/models/flux2.md) |
| **LongCat-Image-Edit-Turbo** | First non-FLUX benchmark. The Apache 2.0 editor has a 3.87 GB Q4_K_M transformer candidate, plus a Qwen2.5-VL 7B encoder, matching vision projection with GGUF, and an autoencoder. The native-runner guide points to a gated FLUX.1-dev autoencoder. Qualify the autoencoder supplied by Meituan for the native runner and review the complete package before offering a download. Measure total disk and runtime memory before claiming a hardware tier. [Meituan model](https://huggingface.co/meituan-longcat/LongCat-Image-Edit-Turbo) · [Quantised transformer](https://huggingface.co/vantagewithai/LongCat-Image-Edit-Turbo-GGUF/blob/d3a793f6694219123c88f16a60dd3d838c183d91/LongCat-Image-Edit-Turbo-Q4_K_M.gguf) · [Runner guide](https://github.com/leejet/stable-diffusion.cpp/blob/master/docs/models/longcat_image.md) |
| **Qwen-Image-Edit-2511** | First higher-memory workstation candidate. It has an Apache 2.0 licence and a documented native editing recipe with Q4_K_M weights, a specific conditioning setting, Qwen2.5-VL 7B and an autoencoder. The transformer alone is about 13 GB on disk. Assemble and review the complete download for the existing recipe. Benchmark detail and material retention when suitable hardware is available. Qwen-Image-2.1 has a separate research licence and is outside the studio catalogue. [2511 model](https://huggingface.co/Qwen/Qwen-Image-Edit-2511) · [Quantised transformer](https://huggingface.co/unsloth/Qwen-Image-Edit-2511-GGUF/blob/main/qwen-image-edit-2511-Q4_K_M.gguf) · [Runner guide](https://github.com/leejet/stable-diffusion.cpp/blob/master/docs/models/qwen_image_edit.md) · [2.1 licence](https://huggingface.co/Qwen/Qwen-Image-2.1/blob/main/LICENSE) |

The first native-runner fixture totals **5,207,177,908 bytes** before notices. The model repositories report Apache 2.0 for all three files, and an anonymous request for each pinned file's headers confirmed its published size. Verify the file hashes at install time and exercise editing with this exact combination. Compare the small decoder against the full one on thin edges, texture and colour. Reuse this package across supported native runner builds if their model files and recipe match.

| FLUX.2 klein 4B component | Bytes |
| --- | ---: |
| [Distilled transformer, Q4_0](https://huggingface.co/leejet/FLUX.2-klein-4B-GGUF/blob/3b1f5a9dc3abb32238b053aeb3d823c30afdacbd/flux-2-klein-4b-Q4_0.gguf) | 2,460,378,560 |
| [Qwen3 4B text encoder, Q4_K_M](https://huggingface.co/Qwen/Qwen3-4B-GGUF/blob/bc640142c66e1fdd12af0bd68f40445458f3869b/Qwen3-4B-Q4_K_M.gguf) | 2,497,280,256 |
| [Full encoder with small decoder](https://huggingface.co/black-forest-labs/FLUX.2-small-decoder/blob/a3efc24f613ef42d9428af62fdbd6f5fd8856c4a/full_encoder_small_decoder.safetensors) | 249,519,092 |

Use a pinned `stable-diffusion.cpp` build as the next native-runner candidate. Its current upstream documentation covers FLUX.2 klein 4B with a matching encoder and decoder, and CUDA and Metal builds. First qualify the editor on Windows x86-64 with NVIDIA CUDA. Then compare native Metal against the existing MFLUX/MLX route on Apple Silicon. Keep the same Rhino workflow on both platforms and select the runtime based on the measured fit. The Linux CPU test establishes the worker contract on that machine. Windows CUDA and macOS Metal still need native acceptance. [FLUX.2 runner guide](https://github.com/leejet/stable-diffusion.cpp/blob/master/docs/models/flux2.md) · [Runner build guide](https://github.com/leejet/stable-diffusion.cpp/blob/master/docs/build.md)

For recommendations, use the same authored Rhino capture, framing and edit intent for each candidate. Review repeated facade bays, section edges, and visible material boundaries and textures against the source. Record the effective prompt, model canvas, full package size, render time and memory peak. Start with representative laptop and workstation configurations and add results as hardware becomes available.

## Memory while modelling

The existing FLUX.2 klein 4B development profile uses a prepared package of about 4.6 GB on disk. Its 1152 × 896 edit reached 9.72 GiB in peak MLX allocations. The worker already runs out of process, uses a cache limit, and has a sampled resident-memory abort budget. The sample protects against observed worker growth and can miss brief spikes. Measure whole-system pressure alongside a large Rhino document before setting fit guidance. [Engine benchmark and worker limits](../../engine/README.md)

- **Check before loading weights.** Use measured peaks for the exact model, runtime and generation canvas where available. Use credible upstream requirements to guide the initial hardware tier. Leave headroom for Rhino and the OS, and handle allocation failure in the isolated worker. On Apple Silicon, Rhino, the CPU and the GPU draw on shared memory. Check memory pressure alongside available capacity, and account for compression and swap. On Windows with a dedicated NVIDIA GPU, check both system-memory headroom and currently usable VRAM for the selected GPU. Budget readings can change during a job. [Apple unified memory](https://developer.apple.com/documentation/metal/mtldevice/hasunifiedmemory) · [Apple memory pressure](https://support.apple.com/guide/activity-monitor/view-memory-usage-actmntr1004/mac) · [NVIDIA memory queries](https://docs.nvidia.com/cuda/cuda-runtime-api/cuda_runtime_api/group__CUDART__MEMORY.html)
- **Give Rhino room to keep working.** Run one local inference worker at a time. Release its model and buffers on completion, cancellation or a pressure-related stop. Start with per-job loading. Consider short idle retention only where measurement shows a meaningful speed gain with enough headroom, and evict retained weights on pressure or model change. Keep routine panel previews small and completed full-resolution images on disk.
- **Keep the recovery actionable.** If there is too little headroom, keep the prepared reference and explain whether system memory or GPU memory is limiting the job. Let the designer choose a smaller generation canvas or another installed model and retry. Keep the chosen view and export size explicit. Treat model offload and tiled decoding as measured profile options, with their time and memory costs included in the hardware guidance.
- **Test beside a real Rhino project.** On each target machine, test a clean install, a typical architectural document and a heavy texture or geometry workload. Record peak worker and Rhino memory, system pressure or VRAM, swap growth, viewport responsiveness, and time to release memory. Check repeated renders, cancellation during load and generation, and a successful retry after a memory-related refusal. Use these measurements for the catalogue's hardware and generation-size guidance.

## A server you own

Install the server on the machine that will render. Keep its weights and job storage on that machine. In Rhino, enter a memorable connection name, an HTTPS address and an access token, then select **Check connection**. Use the same flow for an office workstation or a machine reached through a private VPN. The check authenticates and discovers compatible image-edit profiles without sending a capture. The first server package should implement VisoLoom's existing capability and job concepts. An existing service can be added through a reviewed connector that supports the same reference-image, capability and result checks.

Confirm the endpoint explicitly, verify its certificate, and store the token in the OS credential store when the user chooses to remember it. Apply origin restrictions to job submission, status requests and output downloads. Keep the chosen destination and exact model fixed from preview through submission. If the server is unavailable, keep the selected model visible with a reconnect action. The user's project keeps its versioned model choice without storing a server address or credential.

### Recommended first pilot: one shared studio GPU

An architecture team with several Rhino seats can install the weights once on an always-on workstation, then submit independent jobs from Mac and Windows over the office network or private VPN. Rhino users already have a precedent for adding network render hosts in V-Ray. VisoLoom's pilot queues complete image edits on one GPU. Each job carries the reviewed capture and instructions, with no Rhino document or asset folder to transfer. This is a product hypothesis to validate with a studio. [Chaos Rhino render nodes](https://support.chaos.com/hc/en-us/articles/52416921895057-How-to-switch-from-Swarm-to-Distributed-Rendering-with-V-Ray-in-SketchUp-Rhino-and-Revit)

| Host scenario | Recommendation |
| --- | --- |
| Dedicated studio workstation with an RTX GPU | First pilot target: an existing Linux x86-64 workstation with NVIDIA CUDA. Test one GPU and one exact commercially usable edit model, then package that profile as a standalone service. NVIDIA documents container GPU access if we use a packaged container. [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html) |
| Existing Windows RTX workstation | First follow-up if a studio already owns one. NVIDIA supports CUDA on WSL 2, so test the Linux runner there as a deployment option. Validate office reachability, startup after reboot, cancellation and contention with desktop Rhino before recommending it. Native Windows local inference remains a separate deliverable. [NVIDIA CUDA on WSL](https://docs.nvidia.com/cuda/wsl-user-guide/) |
| Apple Silicon Mac or DGX Spark | Validate as separate host builds when hardware is available. Both use the same client connection and job workflow once their runner and model pass host checks. |
| Studio already running ComfyUI | Evaluate a connector after the first pilot. Let the operator pin a reviewed reference-image editing workflow on the server. ComfyUI exposes workflow submission, queue and history routes, which can support an adapter once its job and data boundaries are validated. [ComfyUI server routes](https://github.com/Comfy-Org/docs/blob/main/development/comfyui-server/comms_routes.mdx) |

Reuse the current job states and bounds, and add per-user revocable credentials and ownership for each job. Check ownership on status, cancellation and every result download, and scope retry IDs to their owner. Start with one running edit and a bounded shared queue. Give clients queued/running status, cancellation and reconnect. Make capture/result retention an operator setting and keep project data out of routine logs. The current service uses a single session token and a development-only loopback policy. Keep that path separate from the office host.

Acceptance is two Rhino seats submitting authored captures to one GPU. Verify both get their own results, an unauthorised seat cannot read or cancel another seat's job, and losing the connection does not duplicate a render. Record cold and warm render time, peak GPU memory, and turnaround for two queued jobs at representative viewport sizes. Use those measurements to document model fit and team capacity.

### What the host setup guide should cover

1. On the host, install the standalone runner package for that operating system and hardware. Download a supported model there or point the runner at an already verified compatible folder. Check that an edit runs on the host before adding Rhino clients.
2. Start the service independently of Rhino. Set the model and job locations, create revocable client credentials, and provide an HTTPS address reachable on the local network or private VPN. Document how the owner starts, stops and updates it and what happens to queued jobs when the machine sleeps or restarts.
3. From a second machine, add the address under **Your server**, check connection and run an authored test render. Verify that only permitted clients can submit, that each client can only retrieve its own jobs, and that operators understand where captures and results are kept.

Write the first guide for a platform after a real end-to-end test. Apple Silicon Macs and NVIDIA DGX Spark are separate host profiles to validate. Spark uses ARM64 and a CUDA-capable NVIDIA GPU, while today's reviewed Linux runner is x86_64. A Spark guide depends on a tested ARM64 runner and image-editing model. [NVIDIA DGX Spark architecture](https://docs.nvidia.com/dgx/dgx-spark-porting-guide/latest/overview.html) · [Current VisoLoom runner profiles](../../engine/README.md)

After standalone hosting works, evaluate an explicit **Share this computer** action on a supported desktop. It would start the same server using compatible weights already installed on that machine, show the address and let the owner add or revoke access. A normal local model installation stays private to the computer. Sharing is a separate opt-in and must define job limits and who can view or retrieve each result.

## Settings and Render

| Where the model runs | Setup in Settings | What the designer sees |
| --- | --- | --- |
| Local models | **Browse models…** or **Use existing model folder…** | Readiness, storage used, and a folder action. |
| Your server | **Add server…** and **Check connection** | The connection name and models that pass the compatibility check. Model files stay on the server. |
| Cloud | Connect a supported provider with an API key. | Connection status and what will be sent on Render. |

Keep folder, credential and server setup in Settings. The Render panel uses one model selector with a short destination label, such as **FLUX klein · Studio workstation**, and the same preview and render actions everywhere. Preview stays on the designer's computer. Before rendering, show the named destination that will receive the captured image and selected instructions. Keep models that still need setup in Settings, with an explanation and the next available action.

## Consequences

The plug-in carries the runner and keeps model weights in the user model library. Settings shows installation progress, terms and hardware guidance. The development workflow remains available for prepared folders. Runner updates travel with plug-in updates and stay verified at runtime. The existing prototype requires first-edit acceptance for an installed profile. The release design replaces that per-install gate with the runtime checks above and records benchmarks separately. Native platform acceptance still applies to the runner and Rhino integration.

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

## Adapter acceptance (2026-11-15)

The ordered adapter exists and is accepted in development: `engine/sdcpp_backend.py`
serves `flux3-image-klein-q4k` under the engine.py contract, with the pinned
runner staged per platform under the private engine root, receipts carrying the
`sdcpp-80c4707-klein-q4k` profile, and the C# side generalized from one managed
model to the reviewed definition set (wire identifiers unchanged for FLUX.2
klein 4B). Two real edits recorded first-edit acceptance for this profile on the
same floor-tier bench: a 256 x 192 development edit in 51 minutes, and the full
512 x 448 edit submitted through the authenticated reference service in 1 hour
56 minutes (16 steps, ~436 s/step) with the machine also carrying its normal
interactive load and heavy swap pressure — an idle-machine run would sit closer
to the spike's 98 minutes. Both outputs passed the complete frame, hash and
provenance validation, and the service advertises the model as
"FLUX.3 Image Klein Q4K · this computer". Production packaging — the reviewed
catalogue download, runner builds for further platforms, and a measured
per-tier time/memory budget for production frames — remains open exactly as
this record's consequences section states.

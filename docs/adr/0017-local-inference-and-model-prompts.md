# ADR 0017 — Local engines, self-hosted connections and concise editing prompts

Status: Implementation direction from the user's 2026-10-14 request. A Draw Things calibration client is implemented; the production local engine and server connection UI are not.

## Decision

Keep inference outside Rhino. The normal workflow chooses a ready model in the panel; Settings owns connections and installation. Distinguish **This computer** from **Connect to a server**. A connection supplies models with explicit editing inputs, resolution and prompt limits, pass usage and job behavior. The protocol is not named after FLUX and does not accept arbitrary ComfyUI workflows or commands from Rhino.

An eventual VisoLoom-managed companion runs separately from Rhino, with automatic authenticated loopback connection and a small model catalogue. It checks hardware, free space, download size, model terms and content hashes before an explicit install. It can reuse compatible existing weights after verification; sharing an app's model directory is not a blanket compatibility claim. Distribution, runtime choice, updater and model installation are later implementation work, not promised by this experiment.

Home/office servers use the same capability/job concepts, authenticated HTTPS and explicitly approved origins. Support other editing models through adapters. Office policy and egress enforcement belong to the server operator. Do not expose Draw Things' unauthenticated HTTP API as an office connection or remove the reference server's authentication.

Separate the neutral scene package from model conditioning. Retain camera/material/context records privately for provenance, but send only what the adapter actually uses. Start the local comparison with the reference PNG, style and user instructions, and the preservation objective once. Omit camera matrices, coordinates, material swatches and unmapped names from this prompt. Additional passes/material semantics need a measured benefit and a genuine conditioning input; they must not be described as consumed just because a server received JSON.

## First experiment

Use the already installed **FLUX.2 klein 4B** in Draw Things on this M1 Pro/16 GB Mac. Its selected normal variant is `flux_2_klein_4b_q8p.ckpt`. No new model download, ML runtime or packaged dependency is needed for the experiment. Model switches and server enablement are explicit; no image generation starts on discovery.

The small `DrawThingsEditClient` is a calibration transport, not a ready `IRenderProvider` or a server-protocol implementation. It sends one explicit base64 `init_images` input to `/sdapi/v1/img2img`, checks the selected model, disables additional LoRA/control/refiner/upscale/batch behavior, rejects unknown output dimensions and never retries generation. Stopping local waiting does not establish remote cancellation. No redirects, DNS hostnames, LAN origins or output URL fetching are allowed.

The HTTP API inherits some application settings and reflects generation in the Draw Things project. It cannot attest that server/cloud offloading is disabled, enumerate all installed models or supply the needed durable job contract. Check local execution and record the inherited settings before using a confidential scene. A successful HTTP image proves neither complete prompt consumption nor geometric fidelity.

Draw Things' model specification declares a padded text length of 512 for this model. The experiment rejects prompts over 2048 UTF-8 bytes without modifying the user's words. This is a conservative experiment budget, **not a tokenizer-equivalent guarantee**. Confirm actual token handling before production. The existing OpenAI prompt is unchanged; evaluate a concise OpenAI variant separately rather than silently altering its behavior.

## Alternatives and next gate

Embedding model inference inside Rhino would burden its process and dependency graph. Requiring Draw Things for every installation ties product setup to one application. A generic workflow editor shifts inference configuration onto designers. Direct unauthenticated LAN connections violate the accepted privacy boundary.

Compare a short-prompt edit of a representative scene before implementing panel model discovery. The next gate is a stateless, verifiable local backend with explicit capability/conditioning limits, bounded single-worker jobs, durable submission identity and honest cancellation. Evaluate the official headless engine against the existing server contract; do not write an installer until the runtime, licence and hardware tests pass.

Evidence: [official Draw Things HTTP route implementation](https://github.com/drawthingsai/draw-things-community/blob/main/Libraries/HTTPAPIServer/Sources/HTTPAPIServer.swift), [request/response implementation](https://github.com/drawthingsai/draw-things-community/blob/main/Libraries/HTTPAPIServer/Sources/ServerModels.swift), [model specifications](https://github.com/drawthingsai/draw-things-community/blob/main/Libraries/ModelZoo/Sources/ModelZoo.swift), [BFL model card](https://huggingface.co/black-forest-labs/FLUX.2-klein-4B). Reviewed 2026-10-14. Model weights remain separate; VisoLoom licensing remains undecided and no LICENSE is added.

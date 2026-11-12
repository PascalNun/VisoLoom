# FLUX 3 Image Klein review

Reviewed 2026-11-12. **Candidate for a separate local adapter; not selectable in Rhino.** Keep the working FLUX.2 klein 4B connection until the new pipeline passes architectural comparisons.

BFL released the 5.6B image model on October 15, with distilled and base checkpoints. Its image-editing and layout features make it worth evaluating for VisoLoom. Advertised speed on server GPUs does not establish performance alongside Rhino on a designer's computer. [BFL release](https://bfl.ai/blog/flux-3-image-klein).

## The prompt path changes

The official model card specifies full scene captions, including for editing. Its default pipeline expands the instruction using the references before inference. The recommended upsampler is an additional 9B model, with an 18.8 GB package. An already complete caption can bypass that stage. The image transformer also needs a separate text encoder and autoencoder; transformer size alone is not the install or memory budget. Boxes guide placement but are not hard masks, and references do not lock structure. [Model card](https://huggingface.co/black-forest-labs/flux-3-image-klein/blob/main/README.md).

For VisoLoom, this means a dedicated preparation contract. Retain the designer's instruction separately from any expanded caption. Show the effective caption for review, freeze it with the source image and record the caption model/version if used. An expansion must not invent architecture, change material intent or become an undisclosed cloud request. Do not reuse FLUX.2's short-prompt/token-limit profile or silently expand text after submission. These are proposed requirements, not implemented behavior.

## Native runner and existing evidence

The runner's own FLUX 3 instructions show reference editing with a separate diffusion model, VAE and Qwen3-VL encoder, 16 steps and explicit image input. They also link GGUF conversions. This supports a separate runner profile; it does not establish compatibility with Draw Things checkpoints or clear converted artifacts for distribution. [Runner instructions](https://github.com/leejet/stable-diffusion.cpp/blob/master/docs/flux3.md).

[ADR 0022](../adr/0022-engine-bundling-and-one-click-model-install.md#spike-outcome-2026-11-08) records the other machine's successful CPU edit and approximately 6.4 GB model package, but also a 98-minute runtime and failed HD 620 Vulkan execution. Preserve that evidence as a backend feasibility result. It does not establish usable interactive speed, Windows-native acceptance or geometry fidelity. Its package size excludes a new captioning model. No runner, conversion or model download is added by this review.

## Terms and availability

The FLUX Kommunity License provides conditional commercial-output rights for qualifying users below US$5M annualized revenue including affiliates. It also sets review/filtering, attribution and distribution conditions. It is not an unconditional permissive license for every architecture practice. A catalogue entry needs the actual terms and an eligibility path before installation; converted weights and runtime dependencies need their own provenance review. This review does not approve redistribution or accept terms for the user. [Official license](https://huggingface.co/black-forest-labs/flux-3-image-klein/blob/main/LICENSE.md).

## Next implementation gate

Start with one authored full caption and the existing native-runner spike profile. This isolates editing quality without installing another language model. Compare it with the working FLUX.2 and OpenAI paths using frozen authored sources: a numbered facade, a clipped stair/section and a material/planting scene. Test an ordinary rendered capture and its matched restrained-edge version. Record source, model and export sizes, border landmarks, opening/step counts, material regions, shadow direction, elapsed time and memory. Repeat outputs to observe variation; judge architecture separately from style.

Only then implement the adapter's pinned runner identity, frame limits, preparation provenance, cancellation and verified output handling under the existing engine contract. Add captioning only after testing whether it improves preservation and measuring its full storage/memory cost. Keep model downloading, terms acceptance and hardware guidance in Settings; keep the ordinary Render workflow shared across local and cloud models.

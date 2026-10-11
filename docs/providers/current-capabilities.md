# Current provider research

Initial research: 2026-09-19. OpenAI implementation updated 2026-10-11 against official documentation. The local reference adapter and one pinned OpenAI image-editing adapter are implemented. OpenAI has simulated request/response tests; a paid request and account access remain unverified. BFL, Google, xAI and local FLUX remain deferred. The other model names below are dated research examples.

## BFL / FLUX

The current FLUX.2 editing guide distinguishes up to **8 API references** from 10 playground images, describes output up to **4 MP**, and returns an ID/polling URL. Output URLs expire after ten minutes. `x-key` authentication and image-URL inputs are documented. Prefer a stable model such as `flux-2-pro` over the moving preview endpoint for a reproducible integration baseline. [Editing guide](https://docs.bfl.ai/flux_2/flux2_image_editing).

The pro endpoint exposes width/height (documented minimum 64), optional seed, PNG/JPEG/WebP, optional webhooks, and cost/input/output-megapixel fields. Its schema does not fully specify the maximum dimensions, multiples or aggregate upload limits. Base64 acceptance also remains unresolved in that selected schema. Do not substitute a family-level assumption for these gaps. Automatic prompt expansion is enabled by default; the proposed adapter should use `disable_pup` to retain the effective prompt VisoLoom records. [FLUX.2 pro API](https://docs.bfl.ai/api-reference/models/generate-or-edit-an-image-with-flux2-%5Bpro%5D).

FLUX.2 and earlier FLUX Fill/Canny/Depth endpoints must not be conflated: a separate controlled model is not evidence that ordinary FLUX.2 reference inputs accept those controls. [Official endpoint index](https://docs.bfl.ai/llms.txt).

No confirmed cancellation or deterministic geometry guarantee was established for the proposed pro editing adapter. Until verified, use client-only stop-waiting semantics. Pricing varies by model and processed input/output; store dated estimates rather than one “FLUX price.” [BFL pricing](https://bfl.ai/pricing).

## OpenAI

Implemented profile: `gpt-image-2.5-sunburst-2026-09-08`, direct JSON `/v1/images/edits`, one reference/output, PNG, opaque background, medium quality. Application caps: prepared input 14 MiB, output 32 MiB and response JSON 45 MiB. No `input_fidelity` flag is sent: its documented applicability must not be assumed for this snapshot. The exact composed context, source hash, native/final plan and result hashes are recorded. Native codec acceptance is pending. See [ADR 0012](../adr/0012-first-openai-image-edit.md).

The current guide covers `gpt-image-2.5-sunburst` and `gpt-image-2.5-flare`; editing precision makes Sunburst a candidate for evaluation. Direct Image API edits fit one-shot VisoLoom requests. Current custom-size rules include multiples of 16, ratios from 1:3 to 3:1, maximum edge 3840, and 655,360–8,294,400 pixels; above 2560 × 1440 is experimental. PNG/JPEG/WebP and transparency options are documented. Masks guide edits but do not enforce exact boundaries. [Image guide](https://developers.openai.com/api/docs/guides/image-generation).

The edit reference lists up to 16 inputs, 1–10 results, optional high/low input fidelity, partial-image streaming, and dated September 8 model snapshots. JSON URL/data inputs have a 20,971,520-character limit per URL field. The guide separately describes image/mask uploads below 50 MB; request encoding and endpoint-specific limits need validation rather than treating these numbers as equivalent. Start with one input/output and conservative application caps. [Edit reference](https://developers.openai.com/api/reference/resources/images/methods/edit).

Bearer API-key authentication is documented in the guide's HTTP examples. No server-side cancellation contract for the direct Image API was established; do not assume Responses cancellation applies to it. Pricing is model/quality/size dependent, with token accounting; this adapter uses separate API billing. The newer [Sign in with ChatGPT](https://developers.openai.com/siwc/quickstart) supports plan usage for eligible requests, but its [current limitations](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations) explicitly exclude image generation; account login is not a supported replacement for this image-editing connection. [Pricing](https://developers.openai.com/api/docs/pricing).

## Google Gemini image generation

Current examples include `gemini-3.1-flash-image`, `gemini-3-pro-image`, and a Flash Lite image variant, with distinct limits. The guide supports text/image editing, multi-reference workflows up to 14 images for applicable models, selected aspect ratios, and model-dependent 1K/2K/4K tiers (Flash adds 512px; Lite is 1K). These are tables of actual pixel sizes, not arbitrary width/height. Generated images include SynthID. [Image guide](https://ai.google.dev/gemini-api/docs/image-generation).

The page contains an apparent naming inconsistency: a table heading references “3.1 Pro Image” while model-selection text identifies 3 Pro Image. Recheck the selected model's endpoint response/schema before freezing any capability row. No native material/semantic/depth input or exact region-lock promise was established for this workflow.

Inline requests are limited to 20 MB including text and bytes; Files API is an alternative for larger/reused inputs, subject to retention review. [Image input guide](https://ai.google.dev/gemini-api/docs/image-understanding). Authentication uses `x-goog-api-key` for the Developer API; Vertex authentication is a separate deployment path. [API keys](https://ai.google.dev/gemini-api/docs/api-key).

The current guide demonstrates the Interactions surface; do not copy older generateContent payloads into it. Interactive requests and Batch API jobs have different lifecycle semantics. Batch cancellation does not prove interactive inference cancellation. Verify the exact surface/version at adapter implementation. [Batch API](https://ai.google.dev/gemini-api/docs/batch-api). Paid/free tier data handling and image pricing must be checked for the chosen account; no default use of an unreviewed free tier for confidential projects. [Pricing and tier information](https://ai.google.dev/gemini-api/docs/pricing).

## xAI / Grok Imagine

Current docs use `grok-imagine-image-2.0`, with `/v1/images/edits`, bearer API keys, and URL/base64/File-ID inputs. [Editing](https://docs.x.ai/developers/model-capabilities/images/editing). Multi-image edits accept up to five inputs and default to the first input's ratio; an explicit supported ratio can override it. That does not establish exact arbitrary-size preservation. [Multi-image editing](https://docs.x.ai/developers/model-capabilities/images/multi-image-editing).

Generation docs expose selected aspect ratios, 1K/2K resolution tiers, up to ten outputs and URL/base64 responses. Confirm corresponding edit controls and decoded dimensions before advertising them. No arbitrary pixel-size, exact mask, depth/semantic control, percentage progress, or confirmed remote image-cancellation guarantee was established in the reviewed edit surface. [Generation](https://docs.x.ai/developers/model-capabilities/images/generation).

The documentation also has a dated retirement notice for an older image-quality model. Model catalogue changes need explicit migration, not a silent default swap. [Retirement notice](https://docs.x.ai/developers/migration/imagine-image-quality-nov-2), [models and pricing](https://docs.x.ai/developers/models).

## What remains unresolved before each adapter

| Check | BFL | OpenAI | Google | xAI |
| --- | --- | --- | --- | --- |
| Reference/edit workflow | Documented | Documented | Documented | Documented |
| Native structured scene maps | Model-specific; unverified for proposed endpoint | Not established | Not established | Not established |
| Remote image cancellation | Not established | Not established for direct edits | Verify interactive surface separately | Not established |
| Upload limits | Freeze exact input/aggregate caps from endpoint | Reconcile encoding-specific caps | 20 MB inline; verify Files limits and retention | Exact per-file/aggregate limits unresolved |
| Native resolution | 4 MP family claim; freeze endpoint limits | Constrained custom sizes; experimental high resolution | Tier/ratio tables; resolve naming inconsistency | Tier/ratio controls; exact edit pixels need verification |
| Output normalization | PNG/JPEG/WebP exposed; request PNG explicitly | PNG available | Verify MIME and intended image part | Verify encoded format before PNG export |
| Privacy and account access | Terms/account review required | Terms/account review required | Paid/free/Vertex distinction required | Terms/account review required |

“Not established” means no implementable guarantee was confirmed in this review; it is not a claim that no API could ever support the feature. Do not advertise any unresolved property as available.

For the first real experiment, use one non-confidential reference per request, disable tools/web grounding, pin model/version where supported, and compare border landmarks, opening count, section voids, stairs, and material-region consistency. Evaluate architectural errors separately from style preference, latency, native size, and cost. Request retries must not create duplicate billable work. A seed, even if accepted, does not guarantee repeatability across runtime/model updates.

The provisional first recommendation is OpenAI direct edits after the local reference slice, because it keeps installation and initial request scope small. If local-only policy is mandatory, choose and license a separate local pipeline instead; an API preference cannot override project privacy.

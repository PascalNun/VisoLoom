# ADR 0021 — BFL adapter scaffold

Status: The FLUX.2 Pro scaffold was implemented on 2026-11-06. The 2026-11-20 follow-up selected hosted FLUX 3 Image Klein as the first profile to complete; it completed on 2026-11-25 and renders (ADR 0027's resolution records the documented inline contract and the full flow). The FLUX.2 pro scaffold stays out of Render until its own inline input and framing contract is verified.

## Decision

Add `BflImageProvider` in `VisoLoom.Providers`, pinned to `flux-2-pro` at `https://api.bfl.ai/` with `x-key` authentication, following the accepted adapter contract. BFL documents no model-list surface, so `DiscoverAsync` returns the pinned capabilities without a request or credential use; this is recorded rather than inventing a discovery endpoint.

Rendering fails closed on two explicit gates, in order:

1. `sizeContractUnpinned` until endpoint research pins the documented output limits (4 MP family claim, width/height minimum) to verified edit pixel tables. The constructor's optional pinned table is the same research seam as ADR 0020.
2. `referenceUploadUnresolved`, because the pro schema documents hosted image-URL references while base64 acceptance is unresolved, and VisoLoom hosts no images. No reference host is contacted and no image is sent.

The async submit/poll flow and the ten-minute expiring result download are deliberately not implemented behind those gates. The adapter is not selectable: `ProviderCatalogue` keeps it unavailable with a scaffold status, and no Settings page, credential store entry or model-catalogue choice was added. Cloud opt-in, key validation and the fixed HTTPS origin follow ADR 0012 unchanged.

## Hosted profile to implement first

Add **Black Forest Labs** under Cloud providers. Keep its API key in its own secure credential namespace, with the same explicit destination consent and session or saved-key choice as other cloud connections. The public schema lists an authenticated `GET /v1/credits`. Use it to check the key without submitting project data. Read account access separately from validated model compatibility. [BFL OpenAPI schema](https://api.bfl.ai/openapi.json)

1. Adopt the explicit `flux-3-image-klein` profile at `POST /v1/flux-3-image-klein` with `x-key`. Send `prompt`, one prepared PNG as base64 in `images`, and `output_format: "png"`. The public schema documents inline base64 in this field. Start with the first reference's default size behavior. Exercise the planner with a known frame and verify returned dimensions before enabling wider sizing. The service runs prompt upsampling automatically, so record the text sent and identify it as the submitted prompt. [API schema](https://api.bfl.ai/openapi.json) · [FLUX 3 Image Klein guide](https://docs.bfl.ml/flux_3/flux3_image_klein.md)
2. Persist the accepted job ID for bounded status polling. Validate a returned polling URL against the documented BFL API origins before attaching the key. Use a separate result downloader limited to the documented `delivery.<region>.bfl.ai` host pattern, with HTTPS, redirects disabled, bounded bytes and decoded PNG size checks. Keep keys off downloads, and keep signed delivery URLs out of history and logs. Fetch the result promptly and save it through the existing local result flow. [BFL integration guide](https://docs.bfl.ai/api_integration/integration_guidelines)
3. Use **Stop waiting** while the service owns the job. An uncertain or timed-out submission does not trigger a second POST. Continue only status polling where a verified job ID is available. Exercise authentication, unexpected origins, moderated and failed jobs, expired results and mismatched dimensions through fixtures. Confirm a single authorised live edit with a non-confidential Rhino capture before listing the profile in Render.

Compare hosted FLUX.2 klein 9B and the FLUX.2 Pro scaffold later. Keep Pro disabled until its own inline input and framing contract are verified. BFL lists FLUX.2 dev as open weights for self-hosting. The older FLUX.1 dev endpoint `/v1/flux-dev` has an image-prompt generation contract. Expose it only if that contract turns out to fit whole-view Rhino editing. [BFL API schema](https://api.bfl.ai/openapi.json) · [Hosted model availability](https://help.bfl.ai/articles/7986977817-what-are-the-costs-associated-with-using-your-models)

## Official references

- [FLUX.2 editing guide](https://docs.bfl.ai/flux_2/flux2_image_editing) and [FLUX.2 pro API](https://docs.bfl.ai/api-reference/models/generate-or-edit-an-image-with-flux2-%5Bpro%5D).
- [BFL integration guidelines](https://docs.bfl.ai/api_integration/integration_guidelines) for polling, result delivery and the ten-minute expiration.

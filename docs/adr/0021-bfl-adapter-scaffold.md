# ADR 0021 — BFL adapter scaffold

Status: Implemented as a fail-closed scaffold on 2026-11-06, completing the researched provider set alongside ADR 0020. No live request, credential or paid work has been made through it.

## Decision

Add `BflImageProvider` in `VisoLoom.Providers`, pinned to `flux-2-pro` at `https://api.bfl.ai/` with `x-key` authentication, following the accepted adapter contract. BFL documents no model-list surface, so `DiscoverAsync` returns the pinned capabilities without a request or credential use; this is recorded rather than inventing a discovery endpoint.

Rendering fails closed on two explicit gates, in order:

1. `sizeContractUnpinned` until endpoint research pins the documented output limits (4 MP family claim, width/height minimum) to verified edit pixel tables. The constructor's optional pinned table is the same research seam as ADR 0020.
2. `referenceUploadUnresolved`, because the pro schema documents hosted image-URL references while base64 acceptance is unresolved, and VisoLoom hosts no images. No reference host is contacted and no image is sent.

The async submit/poll flow and the ten-minute expiring result download are deliberately not implemented behind those gates. The adapter is not selectable: `ProviderCatalogue` keeps it unavailable with a scaffold status, and no Settings page, credential store entry or model-catalogue choice was added. Cloud opt-in, key validation and the fixed HTTPS origin follow ADR 0012 unchanged.

## Remaining gates

Resolve base64 acceptance or define an authorized reference-hosting route, pin the size table, then implement the submit/poll/result-download flow with client-only stop-waiting semantics and a host allowlist for `*.bfl.ai` delivery URLs. After that, run the same comparison protocol as the other candidates: one frozen non-confidential reference, identical fixtures, and separate evaluation of geometry, native size, latency and cost.

## Official references

- [FLUX.2 editing guide](https://docs.bfl.ai/flux_2/flux2_image_editing) and [FLUX.2 pro API](https://docs.bfl.ai/api-reference/models/generate-or-edit-an-image-with-flux2-%5Bpro%5D).
- [BFL integration guidelines](https://docs.bfl.ai/api_integration/integration_guidelines) for polling, result delivery and the ten-minute expiration.

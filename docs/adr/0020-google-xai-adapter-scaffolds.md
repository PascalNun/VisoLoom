# ADR 0020 — Google and xAI adapter scaffolds

Status: Implemented as fail-closed scaffolds on 2026-11-06, at the user's request to begin the multi-provider comparison. No live request, credential or paid work has been made through them.

## Decision

Add two adapter scaffolds in `VisoLoom.Providers`, following the accepted adapter contract and the OpenAI adapter's structure: `GoogleImageProvider` pinned to `gemini-nano-banana-2.1` on the Gemini Interactions surface (`/v1beta/interactions` with `x-goog-api-key`), and `XaiImageProvider` pinned to `grok-imagine-image-2.0` on the xAI JSON edit surface (`/v1/images/edits` with a bearer key). Each composes one inline PNG reference plus the exact recorded prompt, checks the pinned model during discovery without an image, decodes one returned image, normalizes it to the complete Rhino frame and records the same provenance as the OpenAI adapter. No package or ML runtime dependency is added.

Both scaffolds fail closed where the reviewed endpoint contracts are unresolved:

- Rendering refuses with `sizeContractUnpinned` until endpoint research pins the documented Google 1K/2K/4K tiers and xAI 1K/2K edit tiers to verified pixel tables. A constructor-injected pinned table exists only as the research seam; the default keeps the adapters closed.
- The xAI adapter refuses hosted result URLs with `urlResultNotSupported` until the download contract (host allowlist, expiry, encoded format) is researched. Base64 results are accepted.
- Neither adapter is selectable: `ProviderCatalogue` keeps both `Available = false` with a scaffold status, and no Settings page, credential store entry or model-catalogue choice was added.

Cloud opt-in, key validation, fixed HTTPS origins, disabled redirects, no automatic billable retries and sanitized errors follow ADR 0012 unchanged.

### URL-result policy decision (2026-11-10)

The owner decided the xAI URL-result policy: results may be fetched from
exactly the xAI result origin - one fetch per job, never a wider download
surface - and the decoded image is saved to this machine locally, with the
same provenance, retention and comparison handling as inline results. This
is an explicit, documented exception to the no-output-URL-fetching rule
formulated around the OpenAI flow, not a blanket permission: any other
host, any redirect, any retry or any unlisted format still fails closed.
The live-endpoint research (the exact result host, the link expiry
behaviour and the encoded response format) must still be recorded before
`urlResultNotSupported` is removed; the policy direction itself is settled.

## Remaining gates

Pin the size tables from the live endpoints, record the xAI result-host research per the decided URL policy (2026-11-10), then run the same comparison protocol as the first real integration: one frozen non-confidential reference, concise preservation/preset instructions, identical fixtures, and separate evaluation of openings, section voids, stairs, border landmarks, material regions, native sizes and cost. Native Settings, credential storage and panel integration are separate follow-up slices after the comparison selects a candidate.

## Official references

- [Gemini image generation (Interactions)](https://ai.google.dev/gemini-api/docs/image-generation) and [Interactions API](https://ai.google.dev/api/interactions-api).
- [xAI image editing](https://docs.x.ai/developers/model-capabilities/images/editing) and [multi-image editing](https://docs.x.ai/developers/model-capabilities/images/multi-image-editing).

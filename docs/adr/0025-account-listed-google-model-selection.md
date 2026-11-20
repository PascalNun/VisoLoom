# ADR 0025 — Google account-listed model selection

Status: Owner decision on 2026-11-20 ("implement all the providers; start with the Google one; read Google's documentation"). ADR 0024's pattern extends to Google; the size-contract gate it replaces is recorded here because this surface takes no size field at all.

## Decision

The Render panel's Google rows come from the authenticated `models.list` call (`v1beta/models?pageSize=1000`, bounded like the OpenAI list: at most 1,024 entries and 1 MiB of JSON). The family is identified by ids carrying `-image` or `nano-banana`; text models are filtered; rows keep the provider's listing order because the listing carries no creation dates. The connection check fetches this list and keeps it, exactly like the OpenAI connection check; Settings owns the Google API key in the OS credential store under its own origin, and the shared scene-names toggle governs both cloud providers' prompt composition.

The contract differs from OpenAI's in one documented way: the Interactions request carries no canvas size. The published documentation shows `aspect_ratio` and `image_size` knobs without enumerating their valid values, so none are sent — nothing is invented. VisoLoom plans the prepared input canvas on the documented family contract (a 16-pixel grid inside the 1K–4K generation range, arbitrary aspect within 3, up to 16.8 megapixels) and normalizes whatever canvas the model returns back into the complete Rhino frame. Since the request pins no size, there is nothing for a size-rejection fallback to catch: a rejection is an ordinary, never-retried failure. When verified tier values exist for a specific model, promoting it to a compiled "tested" profile can pin them as that profile's contract.

Provenance, consent and billing posture are unchanged from ADR 0024: each render records the snapshot id as the model id and version, project pins store provider/model/version as before, a document-supplied cloud pin renders only after its explicit in-session choice, no submission is retried automatically, and the loopback/credential boundaries of earlier ADRs are untouched.

## Consequences

- New Google image models (Lite, Pro, later Flash image variants) are selectable the day the account lists them.
- The `sizeContractUnpinned` gate is superseded by the documented planning contract; tier-table research remains a promotion path for pinning verified response-format values per model, not a selection gate.
- Returned native canvases may differ from the prepared input; each result records the actual native dimensions and hashes, and the comparison view restores the complete Rhino frame.
- The pinned model remains the row marked "tested" with its precise fixture evidence behind it; other rows render on the documented family contract, unverified per model — the ADR 0024 posture.

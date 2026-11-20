# ADR 0024 — Account-listed OpenAI model selection

Status: Owner decision on 2026-11-20 (forward compatibility over pre-verification). The authenticated account model list, not only a compiled profile, is the OpenAI selection source. This supersedes the "pinned profile is the only selectable model" default of the model selection plan's first milestone for OpenAI; the local profiles are unaffected.

## Decision

The Render panel's OpenAI rows come from the authenticated `GET /v1/models` list fetched at every connection check. Rows are filtered to the image-edit family (`gpt-image-*`), newest first, because the list carries model names, never what the models accept. The tested snapshot keeps its compiled profile with the reviewed custom-size contract and is the row marked "tested". Every later snapshot the account offers is selectable immediately on the family baseline contract: the standard canvas set the whole family has accepted (1024×1024, 1024×1536, 1536×1024), one reference, one opaque-background PNG, no snapshot-specific parameters.

Provenance is unchanged: each render freezes the actual model id as the recorded version, so what produced an image stays auditable. An explicit in-session choice remains required before a document-supplied cloud pin renders, whatever model is chosen next — the spend still bills the reader's account. A pin naming a model the account no longer lists stays visible, states that it is unavailable, and the compatibility catalogue's retirement wording still applies.

## Posture

New models are usable the day the account lists them. A provider-side change that breaks the family baseline request surfaces as a failed render with an honest error and no automatic retry; the fix is a normal plug-in update. The owner accepts render-time failure as ordinary maintenance and does not treat a pre-release comparison as a selection gate. When a snapshot is later adopted into a compiled "tested" profile, the existing comparison protocol is the adoption evidence; it promotes model rows, it never withholds them.

## Consequences

- Retirement of the tested snapshot no longer blocks cloud rendering while a successor is compiled: the menu already shows everything the account offers.
- An unverified snapshot can reject the family baseline or silently change edit behaviour; results remain attributable per render through intent/result provenance, but the plug-in does not pre-verify new snapshots.
- The compatibility catalogue's recommendation data now only marks which profile was reviewed; it restricts nothing.
- Google, xAI and BFL keep their own research gates; this record sets the pattern their selection flows follow when their contracts are pinned.

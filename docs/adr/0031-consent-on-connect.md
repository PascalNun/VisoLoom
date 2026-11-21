# ADR 0031 — Consent on connect, keys remembered by default

Status: Decided by the owner on 2026-11-22. Changes the consent wording in the model-selection plan's availability section.

## Decision

Entering a provider's API key and clicking connect is the consent to render with that provider. No separate allow-rendering control exists, before or after connection; an extra confirmation of the same act adds no protection. Keys are stored in the OS credential store by default; the explicit action is forgetting the key, not remembering it. Connection checks stay read-only.

The provider's row keeps stating what its render request transmits — the source image, prompt and selected scene guidance — so the disclosure survives; only the second consent step is removed. Session-only keys remain possible by forgetting the key after use.

## Consequences

The connections flow removes the allow-rendering control and the remember-key choice. The Settings mockup audit implements this with the rest of the recorded design, and the connection fixtures change with it. Nothing else about credentials, destinations or transmitted fields changes.

# ADR 0032 — Gated weights download with the user's Hugging Face token

Status: Decided by the owner on 2026-11-23. Amends the local catalogue's gated-model flow from the recorded inventory's conservative publisher-link flow.

## Decision

Licensed or gated model weights become in-app downloads using the user's own Hugging Face access token. The licence acceptance never moves into the app: acceptance stays on the publisher's page, in the user's name, and VisoLoom never presents, collects or stands in for it. The token is entered by the user in Settings, stored in the OS credential store, and sent only as the download request's authorization header to pinned huggingface.co files. Forgetting the token removes it.

## Rationale

The owner asked for the easiest flow a user can honest get, and flagged the legal risk of interposing the product in the licence relationship. The two halves separate cleanly: collecting acceptance is the part legal exposure, and it stays with the publisher; fetching files the user has rights to is a download the user could make by hand, and doing it on their behalf with their token is the same act. A 403 from the publisher means the user has not accepted there yet; the row says so and links the publisher page.

## Consequences

- Settings gains one Hugging Face token field under Local models, stored beside the provider keys, with a forget action and a hint linking the token page.
- Reviewed packages may mark individual files as needing the token. Those pins must be captured with an account that accepted the licence; hashes enter the manifest, the token never does.
- FLUX.3 Image Klein and its Base variant are the first gated pins (22.11.2026). Pin capture is one-time curation per file revision — Hugging Face hides a gated file's digest from every other source — while a user's own acceptance matters only at download time: one build serves every user, each with their own token and their own acceptance on the publisher's page.
- Get weights… remains for models whose terms are not just a download gate: the non-commercial BFL models keep their licence links, and a model without a reviewed quantised source (LLaDA today) keeps the publisher path.
- The engine download path sends the token as a header without writing it to logs or state; the CLI carries it in memory from the settings layer.
- The flow behaves identically on macOS and Windows: the existing credential store covers Keychain and Credential Manager, the token travels in memory, and no component takes a platform-specific path.

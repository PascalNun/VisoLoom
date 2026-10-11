# ADR 0012 — First OpenAI reference-image edit

Status: Implemented within the user's selected OpenAI-first milestone on 2026-10-11. Automated transport checks pass; real generation and native Settings/codec acceptance remain open.

## Decision

Implement one direct OpenAI Image API adapter, pinned to `gpt-image-2.5-sunburst-2026-09-08`. Send one reference image and receive one opaque PNG at medium quality. Check the account's access to this exact model; do not silently substitute a model. Other cloud providers and local FLUX remain deferred. This adds no package or ML runtime dependency.

Register a native Eto connection page in Rhino Options/Preferences and reuse it in the panel's Settings shortcut. Explicit **Check and save connection** verifies the model without an image upload. Require the user's own API key and explicit cloud opt-in. The panel offers a direct Model dropdown, seven shipped presets and an optional prompt. Selection alone sends nothing. Running work retains the captured provider, preset, prompt, disclosure choice and plan even when the next selection changes.

Use Keychain or Windows Credential Manager only when the user chooses Remember. Otherwise keep the key in this Rhino session. First-time OS failure reports session-only use; it never writes plaintext. Updating a previous saved key cannot silently leave it behind. Disconnect disables new submissions even if OS deletion fails, then reports that failure. Rhino user settings store only nonsecret preferences. Keys never enter scene/history files, `.3dm`, logs, shell arguments or source control.

## Payload and framing

Send the prepared PNG and the exact recorded composed prompt. The prompt includes the shared preservation objective, preset, original user prompt, and bounded quoted JSON context: camera/frustum, frame transform, units, up to 16 active clipping planes and 32 material summaries. Names/descriptions are off by default; optional names are limited to 32 layers/materials and 120 characters each. Omitted counts appear in the context. No GUIDs, document names, paths, raw geometry, texture files or complete scene manifest are uploaded. Visible image content and user-written prompt text are still disclosed.

Material summaries describe eligible scene objects, not verified pixel regions. The reference remains authoritative. Extra metadata must not cause reassignment or redesign. No additional structural pass or region-lock guarantee is advertised.

Freeze the documented model constraints: multiples of 16, longest/shortest ratio at most 3, edge at most 3840 and 655,360–8,294,400 pixels. Mark native area beyond 2560 × 1440 experimental. Upload cap is 14 MiB prepared PNG, output cap 32 MiB, JSON response cap 45 MiB. Check source bytes/hash, recomputed complete-frame plan and output dimensions. The host codec pads/resamples the complete reference and removes only its recorded padding on return. Keep source, native output, normalized result, prompt, plan and hashes in private bounded local history.

There is no automatic billable retry. An interrupted POST or Stop waiting leaves remote completion/billing unconfirmed. Do not fabricate a recoverable remote job for this synchronous API. Fixed HTTPS origin, disabled redirects and sanitized response errors prevent credentials/raw provider errors from escaping to another destination or UI diagnostics.

## Evidence and remaining gates

64 local tests pass with zero build warnings/errors. OpenAI tests use synthetic images and simulated HTTP, covering exact payload/provenance, padding-plan handoff, unexpected sizes, invalid discovery/output, rejection, cancellation and no retries. These do not prove native image resampling or model fidelity. An isolated process running Rhino 8.16's .NET 7 passed synthetic Mac Keychain write/read/update/delete; native Rhino ACL prompts and Windows persistence still need acceptance. No real key or paid request was available.

The user asked about account login. OpenAI now offers Sign in with ChatGPT for eligible plan usage, but its current documented flow excludes image generation. Keep API-key authentication for this adapter and revisit OAuth when a supported image-editing route exists. Do not copy consumer sessions or change the repository's private/licensing status to obtain access.

## Official references

- [Image generation guide](https://developers.openai.com/api/docs/guides/image-generation) and [edit request contract](https://developers.openai.com/api/reference/resources/images/methods/edit).
- [Sign in with ChatGPT](https://developers.openai.com/siwc/quickstart) and [plan-usage limitations](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations).
- [Rhino options pages](https://developer.rhino3d.com/en/guides/rhinocommon/options_pages_best_practices/).
- [Apple Keychain](https://developer.apple.com/documentation/security/keychain-services) and [Windows CredWrite](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew).

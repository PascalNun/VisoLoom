# Settings mockup audit

Compares `docs/ux/settings-mockup.html`, the recorded Settings design, with the current implementation in `src/VisoLoom.Plugin/`. Every row below is one checkable change for the next implementation pass; "recorded copy" means the mockup's exact wording wins unless ADR 0031 changes it.

ADR 0031 (`docs/adr/0031-consent-on-connect.md`) amends the mockup in two places: no allow-rendering tick, no remember-key tick. Entering a key and connecting is the consent; keys are kept in the operating system's credential store by default and forgetting is the explicit act.

Sources read for this audit: `ConnectionSettingsPage.cs`, `ConnectionsView.cs`, `LocalModelsControl.cs`, `ImageSavingSettings.cs`, `tests/native/SettingsCheck.cs`.

## 1. Page and headings

- [ ] **A1** Headings and order match the mockup: Local models, Cloud providers, Your server, Image saving, each separated by a full-width hairline. Already correct in `ConnectionSettingsPage.cs`; keep.
- [ ] **A2** Window carries one right-aligned default button, Done. Already correct; keep.
- [ ] **A3** Section headings sit as `h2`-weight rows, not stacked bold labels above the section. Use the shared row caption pattern (title with secondary detail to its right, as in the Image saving row) so "Your server" and others read like the mockup.

## 2. Local models

- [ ] **L1** Installed models appear as one row per model: bold name, secondary status "Installed" (downloaded) or "Existing files linked" (linked folder), green when running. The current single text paragraph (`installed` label in `LocalModelsControl.cs`) is replaced by these rows; "No models installed" shows only when the installed list is empty.
- [ ] **L2** Actions row: "Browse models…" and "Use existing files…" open mutually exclusive insets. Correct today; keep, and focus the folder picker button when the existing-files inset opens (already done).
- [ ] **L3** Catalogue rows: bold name, fit line as secondary text ("Compact · 5.2 GB" style from the catalogue data), Download button on the row, and a per-row status line during installs ("Downloading model files…", "Verifying files…"). The current shared `installDetails` status and the "Model details" expander are folded into the row: fit line where the expander's hardware summary sat, per-row status instead of the shared label.
- [ ] **L4** Models whose weights need a licence acceptance (publisher and licence references on the package) download through a "Get weights…" button that opens an inset: the licence sentence, "Publisher weights" and "Licence terms" links, and "Use existing files…" for pre-downloaded folders. `ReviewedModelPackage` gains the two reference fields; the curated list carries them. Packages without a gate keep the direct Download button.
- [ ] **L5** "More local models" holds the remaining catalogue rows in the same row shape as the main catalogue (L3), not text-only expanders as today.
- [ ] **L6** Download folder row: "Download folder", current path as secondary text, "Change…" opens the native folder picker. Correct today; keep the native picker. When the user returns from the picker, collapse to the row (as the mockup's Change… toggle does).
- [ ] **L7** Existing-files inset: "Choose folder…" (native picker) plus the saved-folder list with per-folder state, and a status line recording the outcome ("Folder added." / the compatibility message "This folder does not match an editing model VisoLoom can run. Choose a compatible model folder."). The mockup's example-folder select is the fixture stand-in for the native picker; keep the picker.
- [ ] **L8** Manage-folder actions (Verify setup, Forget location, Delete downloaded files) attach to the selected folder in the list, not as loose buttons under the inset. Keep the confirmations; they are the recorded destructive-path guards.
- [ ] **L9** The old engine block (Enable local rendering tick, Check local connection, Local setup expander, Manual local connection) is removed. The engine availability line lives with the installed rows: one secondary status "Local engine · Checking… / Running · Model name / Off", acted on by a single "Check engine" button, matching the mockup's assumption that an installed model becomes renderable.
- [ ] **L10** The catalogue and existing-files insets keep their open/closed choice while the Settings dialog is open; no separate persistence beyond that session.
- [ ] **L11** Token row (shipped, [ADR 0032](../adr/0032-gated-weights-huggingface-token.md)): inside the catalogue inset below the Download folder row — "Hugging Face token" caption with live status ("No token saved" / "Token saved"), the masked entry, "Save token" / "Forget", the "Create a token on huggingface.co" link, and the one-line disclosure. Typing alone never stores anything; Save is the only path to the OS credential store. Gated catalogue rows read "Needs token" and unlock exactly when the status turns to "Token saved". Licence acceptance itself never happens in the app; it stays on the publisher's page (L4 covers the reference-only models).

## 3. Cloud providers

- [ ] **C1** The Allow tick ("Allow rendering with …") is removed everywhere, including the fixture state `ConnectionSettings.CloudAllowed` and every `CloudAllowed`/`GoogleAllowed`/… consumer path. ADR 0031: connecting is the consent.
- [ ] **C2** The Remember key tick is removed; `Configure(…, remember, …)` loses the parameter and every provider stores the key in the credential store on connect. The status reduced set becomes "Connected · Key saved" and "No API key"; "Session key" and "Rendering disabled" disappear. Session-only use is forgetting the key.
- [ ] **C3** Row disclosure, per ADR 0031: each provider row states what a render transmits — one source image, the prompt, selected scene guidance — as a secondary line on the row, visible without expanding. The current "Connection details" expander moves out of the inset; its account/billing notes join the row disclosure or the inset copy as single secondary lines.
- [ ] **C4** Row toggle: "Connect…" when no key is stored, "Manage…" when connected; only one provider inset open at a time (correct today), and the inset closes when the provider disconnects.
- [ ] **C5** Connect state, unconnected: key field labelled "<Name> API key", hint "Paste your <Name> API key. Saved in this computer's credential store.", main button "Connect", disabled until the field has text.
- [ ] **C6** Connected inset: "Image models" list with the discovered display names, "Refresh models" with feedback "Checking available models…" → "Model list up to date."; a "Change API key" disclosure whose field label is "Replacement API key", hint "Paste a new API key.", button "Replace key"; and "Disconnect &amp; forget key" (recorded copy uses the ampersand).
- [ ] **C7** The Black Forest Labs row is curated: no Refresh models button; the model list appears from the connection check without a separate refresh. Its rendering-pending status stays as a row disclosure until rendering ships.
- [ ] **C8** Connect and refresh keep their read-only semantics: discovery calls only; 15-second timeout; no image is sent. Error copy today ("Enter an API key.", network line) stays.
- [ ] **C9** OpenAI's inset opens by default when nothing is connected, per the mockup's initial state; otherwise the previously open provider is left closed (no persistence required).

## 4. Your server

- [ ] **S1** The placeholder section ("Connecting to a model on another machine is planned.") is replaced by the recorded rows: title row "No server added" with status "Use a model running on another machine" and button "Add server…"; once added, "Studio workstation" style name is the server address and the status carries "Connected · <engine model name>".
- [ ] **S2** Inset form: "Server address" (a URL), "Server access token, if required" (password field), "Check connection" main button (disabled until an address is entered), "Remove server" once connected. Checking performs a read-only engine request against the address; the engine HTTP protocol already provides it.
- [ ] **S3** Connection settings gain the server address and token storage (address in plug-in settings, token in the credential store) and a connected state; the Render model selector gains the "Your server" group when connected, per the Render section of the mockup.

## 5. Image saving

- [ ] **V1** Header row is the shared row caption: "Image saving", status "Saved automatically to VisoLoom's renders folder" (or "… · Extra copy enabled"), button "Options…" toggling the inset. Copy today says "Additional copy enabled"; switch to the recorded "Extra copy enabled".
- [ ] **V2** Inset: row "Additional copy" with secondary "No additional folder" or the folder path, and one button that reads "Choose folder…" when no copy is set and "Remove copy folder" when set, per the mockup's single-toggle button. The separate "Remove additional folder" button is folded into this toggle.
- [ ] **V3** The automatic-folder block stays (path line, "Open saved images"), below the inset rule; it is the real-storage equivalent of the mockup's status line.

## 6. Tests

- [ ] **T1** `tests/native/SettingsCheck.cs` walks the new layout: reflection state without `CloudAllowed`/`RememberKey`, heading set unchanged, the removed ticks asserted absent, recorded copy asserted present per section.
- [ ] **T2** Saving and panel checks keep passing; the `_ = configureLocal` hook in `ConnectionSettingsControl` is dropped together with the Local engine block (L9).
- [ ] **T3** Full suite run after the pass: the three native fixtures and the build with zero warnings, per the recorded commands in the onboarding notes.

## Deviations kept on purpose

The native folder pickers replace the mockup's in-page example buttons (L6, L7), and BFL keeps its pending-rendering disclosure until that path ships (C7). Both serve the mockup's behaviour with real platform affordances.

## Verification, 22.11.2026

The Settings work merged as commits bd0e8a1 and ca9bcd6 delivers the visual hierarchy: section spacing with contrast, inset rules, quiet secondary status text and a connected colour. It builds with zero warnings and the automated suites pass (152 protocol, 90 Core).

The rest of this audit stayed open after that pass. The gap-fix pass of the same evening closed them in the code:

- **ADR 0031 in code** — the allow-rendering tick, the remember-key tick and the session-key state no longer exist. Connecting stores the key in the OS credential store and failing to store stops the connection; Disconnect and forget key removes the entry. The fixture stages only a stored key, and the walkthroughs describe connect-as-consent.
- **Provider rows** carry the transmit disclosure ("A render sends one source image, your prompt and scene guidance."), the recorded connect/replace/refresh copy ("Model list up to date."), and the first provider opens when none is connected. The Black Forest Labs row hides its refresh button; its curated list comes from the connection check and the row notes rendering is pending.
- **Local models** — one row per installed model (Connected / Installed / Existing files linked), the engine line with Check engine replaces the old engine block and the manual-connection developer path, catalogue rows fold their details into the row with a per-row install status, and the download folder shows the ~ short form.
- **Your server** — the section exists with an address, an optional access token kept in the credential store, and a read-only Check connection against the server's capabilities endpoint.
- **Image saving** — the recorded copy ("Extra copy enabled", "Remove copy folder") with one folder button.

**Open on purpose:** a render through Your server needs the office transport ([ADR 0017](../adr/0017-local-inference-and-model-prompts.md)) and ships with that work, not this one. Native Rhino walkthroughs of the changed Settings pages remain a hands-on step.

## Model catalogue, 23.11.2026

The browse list now carries the eight recorded local models in the mockup's order and composition. Qwen-Image-Edit-2511 joins as the second reviewed download package: four pinned files (18.6 GB), sizes and SHA-256 from each source's headers, captured on 2026-11-23. The licensed or gated models — FLUX 3 Image Klein and its Base, FLUX.2 klein 9B, FLUX.2 dev, LongCat-Image-Edit-Turbo and LLaDA-Image-Turbo — appear as Get weights… rows with the verified component sizes, the publisher's page, the licence to review and Use existing files…. Their pins and sources are recorded in the audit trail below the manifest entries: quantised components verified anonymous, except the FLUX autoencoders, which sit behind Black Forest Labs' licence acceptance and therefore do not belong in a managed download.

Rendering through a family's weights still needs its runner recipe: the engine fails closed with its reviewed-adapter message until each adapter ships and passes a real edit. The model cards state this in their setup line rather than implying otherwise.

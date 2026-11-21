# VisoLoom onboarding: what is built and what remains planned

Written 2026-11-20 as the entry map for a new working session. It complements the [roadmap](../roadmap.md) (direction) and the [ADRs](../adr/README.md) (decisions): its purpose is a complete inventory of everything the repository's own documents already plan, with each item's implementation status and its source, so planned work can be picked up without re-deriving it. Evidence language follows the repository's rule: **implemented** means code exists and is described in the review; **scaffolded** means fail-closed code waiting on decided gates; **planned** means documented as future work; nothing here upgrades the status of any acceptance gate.

Repository state at writing: branch `codex/model-selection` (model selection + refined panel implemented; not yet pushed/merged), 111 protocol and 64 engine tests passing.

## How this repository documents itself

| Document kind | Where | Role |
| --- | --- | --- |
| Decisions | `docs/adr/0001–0023` | Accepted decisions and their gates; read the relevant ADR before changing its area |
| Direction | `docs/roadmap.md` | What is next and why, plus the Library/asset-browser plan |
| Evidence log | `docs/development/phase-4-review.md` | Per-milestone working functionality, stubs, blockers and CI runs |
| Interaction targets | `docs/ux/` | Panel, information architecture, journeys, library |
| Provider research | `docs/providers/` | Endpoint contracts and the unresolved gates per adapter |
| Implementation plans | `docs/development/model-selection-plan.md` | The provider rollout plan behind the current branch |
| Scene contract | `docs/scene-package/`, `docs/architecture/` | Scene Package spec/schema, protocol, domain model |

Work flow that the repository expects: change code near its purpose, extend rather than replace decisions, complete an ADR's remaining gates before advertising a feature, and record native evidence in the implementation review.

## Working today

- **Capture test (local)**: authenticated loopback reference server, unchanged reference return, automatic development handoff, launcher.
- **OpenAI cloud editing**: pinned `gpt-image-2.5-sunburst-2026-09-08` adapter, cloud opt-in, OS-stored key, no automatic billable retries. Real Mac edit + export evidence.
- **Local engines**: prepared FLUX.2 klein 4B (MFLUX, Mac ARM64) and FLUX.3 Image Klein Q4K (stable-diffusion.cpp runner, x86_64 Linux/Wine) through the reference service; receipts, acceptance records, Settings readiness. Developer setup; no automatic installation.
- **Panel/workflow**: View → Representation → Model → Prompt → Output size → Preview (frozen) → Render → Result; Named View browsing (read-only + explicit Show); project model pin stored in the `.3dm`; comparison viewer; automatic durable PNGs + optional extra folder; model catalogue with read-only setup reports; existing-model folder inspection.
- **Server/engine**: Draw Things calibration client (diagnostic only); local history with provenance (`intent.json` / `result.json`). The three cloud adapters are wired; BFL renders only after its owner-blocked reference decision.

## Planned-but-unimplemented inventory

### Track 1 — Native and host acceptance (nothing to design; validation and fixes)

Status: **open** — repeatedly named as the top next step ([roadmap](../roadmap.md#next-validate-capture-and-everyday-use), [phase-4-review](phase-4-review.md) gaps).

- Registered/docked panel acceptance on Mac after the model-selection refinement; narrow and wide panel widths; the [smoke-test checklist](plugin-smoke-test.md) end to end.
- Windows native acceptance: credentials, panel layout/DPI/themes, handoff, capture and saving paths.
- Native result resampling validation; broader display-style/lighting/clipping capture matrix; memory pressure with large Rhino files.

### Track 2 — Capture fidelity and context refinement

Status: **planned experiments** ([roadmap context review](../roadmap.md#proposed-context-review), [material appearance](../architecture/material-appearance.md)).

- Controlled architectural fidelity benchmark per the [comparison protocol](../providers/current-capabilities.md): openings, stairs, section voids, proportions, material regions, planting — on identical authored fixtures, geometry separate from style.
- Concise-prompt vs current detailed OpenAI context experiment on representative scenes (uploaded fields unchanged).
- Material appearance: resolve the actual colour input and provenance, then the texture-summary experiment; legacy diffuse swatches stay labelled until replaced.
- Deliberate display-style comparisons (custom AO/edges vs restrained linework) before any VisoLoom display style exists.

### Track 3 — Model selection rollout completion

Status: **partial** — the [plan](model-selection-plan.md)'s first milestone (profiles, project pins, frozen prepared render, recommendation policy recorded) is implemented on the current branch. Remaining:

- Recommended-model logic: implemented — the bundled catalogue names the tested recommended model per provider, a recommendation change is shown in the status area before the next preview, and local models carry no catalogue recommendation (the running engine's own acceptance remains that signal).
- Local model list: implemented — each local Settings choice names the one reviewed engine profile it installs toward (validated at load and unit-tested), and the Models tab annotates the profile the connected engine itself reports: the connected choice shows the engine's reported model version, and a different local choice states which profile the engine is serving. The installed-folder inventory keeps its own verification state; nothing is presented as active from file presence alone.
- OpenAI model listing: implemented — the connection check keeps every image model the authenticated account list offers, and the Model menu offers them, newest first; the tested snapshot is marked, and later snapshots attempt the reviewed custom-size contract first, falling back once onto the family standard canvas on an explicit size rejection ([ADR 0024](../adr/0024-account-listed-openai-model-selection.md)). Google and xAI now follow the same pattern ([ADR 0025](../adr/0025-account-listed-google-model-selection.md), [ADR 0026](../adr/0026-xai-account-listed-model-selection.md)); BFL offers its documented curated pair behind a credits check ([ADR 0027](../adr/0027-bfl-wired-around-reference-gate.md)).
- OpenAI: compare Sunburst 2.6 and Flare against the pinned baseline before enabling; record the provider default policy; verify the Follow-OpenAI contract (`model` omitted) before offering provider-following selection; explicit migration, never a silent default swap on retirement.
- Per-provider connection settings and credential namespaces as each provider is wired in; migrate existing OpenAI settings without granting new permissions; cache by connection, discard stale results after credential replacement, retain last-known status on transient failure.
- Bundled compatibility catalogue: shipped with the plug-in and validated in code — model ids, profile references, labels and recommendation/retirement data only, entries fail closed unless a compiled profile vouches for them, and a retired project pin says so in the panel. The signed, versioned update channel (schema/signature/revision/required-adapter validation, atomic replacement, VisoLoom's public catalogue URL only) remains planned for a second supported cloud profile.
- Release-check fixtures: availability/retirement, malformed capabilities, pin/reopen, stale preview, pre/post-billable failures; no submission on model checks.

### Track 4 — Google adapter (next comparison candidate)

Status: **wired for account-listed selection** ([ADR 0025](../adr/0025-account-listed-google-model-selection.md)); the fail-closed size gate is superseded by the documented planning contract.

- Implemented: Settings/Google credential in the OS store, account-listed Model rows from the bounded `models.list`, the documented Interactions request (no invented aspect/size values), per-render provenance, pin and consent flows shared with OpenAI.
- Remaining: the first real edit through the panel (live call), native Rhino checks of the menu/settings; promotion of verified tier values to a compiled tested profile if the comparison warrants it; SynthID/privacy review of returned images stays with the user.

### Track 5 — xAI adapter

Status: **wired for account-listed selection** ([ADR 0026](../adr/0026-xai-account-listed-model-selection.md)); the fail-closed gates are superseded by the documented request, and the URL-result policy from 2026-11-10 stays pending in ADR 0020.

- Implemented: Settings/xAI credential in the OS store, account-listed Model rows from the bounded `v1/models` (newest first by the documented `created` field), the documented edit request (inline PNG reference, inline result format, 2k tier first with one verified fallback, documented defaults for aspect ratio, quality and count), JPEG results decoded by the host and re-encoded to PNG, per-render provenance, pin and consent flows shared with OpenAI and Google.
- Remaining: the first real edit through the panel (live call), native Rhino checks of the menu/settings; the URL-result host research only if the hosted flow is ever wanted — inline bytes are what the request asks for; per-tier pixel tables remain the promotion path to a compiled tested profile.

### Track 6 — BFL adapter

Status: **wired around its open decision** ([ADR 0027](../adr/0027-bfl-wired-around-reference-gate.md)); the size gate is superseded by the documented pro contract, and rendering stays closed on the owner's reference decision.

- Implemented: Settings/BFL credential in the OS store, a connection check that validates the key against the documented credits endpoint, the curated documented pair in the Model menu (`flux-2-pro` and `flux-2-pro-preview`), pin and consent flows shared with the other clouds, honest status and disclosure text, and `bflCloud` provenance destinations.
- Owner decision (blocker): the reference-upload contract — BFL documents hosted image-URL references only, no upload or base64 surface exists, and VisoLoom hosts no images. No image is sent and no reference host is contacted until this is decided.
- Then: implement the decided async submit/poll/result-download flow (`*.bfl.ai` delivery allowlist, client-only stop-waiting, keys off downloads; re-verify the expiry — the FLUX.2 guide documents ten minutes, the FLUX.3 guide one hour); `disable_pup` so the recorded effective prompt is what is sent; compare FLUX.2 Pro against cloud FLUX.3 Image Klein per the plan's rollout table.

### Track 7 — Local engine productization

Status: **in development** — prototypes accepted; packaging direction decided ([ADR 0022](../adr/0022-engine-bundling-and-one-click-model-install.md)).

- Settings curated-catalogue install: one explicit download action with purpose, hardware guidance, licence terms and size; pinned URL, content hash, prepare, verify and publish through the existing receipts; a partial or unverified download is never advertised as a model. Hosting/entitlement review so installation needs no third-party account.
- Production packaging: reviewed per-platform native runners ship inside the plug-in folder under `engine/<os-arch>/`; development-copy layout parity; managed companion's start/stop ownership and updater mechanics.
- Hardware tiers: distinguish a modern Vulkan-capable GPU tier from the CPU-only floor tier; measured per-tier time/memory budgets for production frames (replacing the blanket four-hour bound); memory-pressure evaluation alongside large Rhino files.
- Non-Apple first-edit acceptance: Windows/NVIDIA (or Vulkan-class) adapter under the same engine contract, with its own dependency lock and real GPU test; Windows profile ACL and descendant cancellation validation.
- Engine service decisions: persistent supervisor, queue, comprehensive orphan cleanup, retention/packaging of the worker — all named open in the phase-4 review.

### Track 8 — Workflow and UX gaps from the accepted targets

Status: **planned** ([information architecture](../ux/information-architecture.md), [panel wireframe](../ux/panel-wireframe.md), [user journeys](../ux/user-journeys.md)).

- Personal preset editor: named, editable copies of the supplied presets (current dropdown is read-only); persistent representation defaults across sessions.
- Settings connections view: **implemented in its first form** ([ADR 0028](../adr/0028-settings-connections-view.md)) — one saved-connection list with state/reason per destination, per-connection details with supported models, add-connection chooser, provider-correct handoff from the Models tab; native settings checks to re-run. Multiple saved connections per provider, renaming and office servers remain targets.
- Connection defaults and restrictions: project policy filters (local-only projects), saved multi-connection management, reliable cost estimates before submission, a model policy change visible before the next preview and render.
- Framing: safe-frame overlay preview before capture; saved output-size setting alongside a Named View; independent output ratios without silently changing the saved camera (roadmap framing work).
- Result/library management: storage cleanup and gallery UI for durable renders; reopened-history browsing (current history is per session).
- Deferred but decided direction: office connections (authenticated HTTPS, approved origins), corporate proxy support, account login only if image editing becomes supported through it.

### Track 9 — VisoLoom Library (asset browser + material collection)

Status: **planned, not implemented** ([roadmap Library sections](../roadmap.md#rhino-material-collection), [library UX plan](../ux/library.md)).

- Prerequisite extraction work: nested-block and instance metadata resolution; asset descriptions via Rhino Attribute User Text; stable per-asset identity/revision; selected-variation recording.
- Asset production: three recognizable plant types with variations plus adjacent architectural materials; thumbnails from the actual assets; rights records (creator, source, revision, units, mesh size, licence evidence, adaptations); per-asset acceptance in Rhino before any image-model test.
- Browser UI decision (shared panel vs separate dock) after a usable collection exists; curated material collection with ordinary Rhino render materials; evaluate native `.rmtl` before inventing a format.

### Track 10 — Distribution and legal

Status: **open gates** (phase-4 review blockers, [licensing status](../legal/licensing-status.md)).

- Software licence decision and a project `LICENSE` (CI currently fails the build if one appears, by design).
- McNeel SDK/distribution terms confirmation; Yak packaging; no public release exists.
- Runtime redistribution and catalogue-source entitlement review (MLX wheels, ggml runner, model weights) before anything ships.

## Suggested order

The roadmap's own priorities put host validation first, then the model-selection rollout; nothing below overrides that.

1. **Finish Track 1** for the current branch: push/merge `codex/model-selection`, run registered-panel acceptance, fix what it finds.
2. **Track 3 remainder + Track 7 in parallel**: recommendation/discovery logic is small and unblocks provider rollout; the catalogue-install flow is the long-lead packaging work (terms review starts early).
3. **Track 4 (Google)** as the next comparison per the provider review, then **Track 5 (xAI)** and **Track 6 (BFL)** as each one's research gates close.
4. **Track 2** runs alongside every provider/local comparison — it defines what "better" means.
5. **Track 8** interleaves with acceptance work; **Track 9** after the core rendering workflow is validated; **Track 10** before any public step.

## Review findings from the 2026-11-20 security review

Both low-severity findings are fixed in code; their remaining gates are noted here.

- [engine/sdcpp_backend.py](../../engine/sdcpp_backend.py): the runner is started inside the worker's own process group (previously its own session), so the coordinator's single process-group kill covers cancellation, the edit deadline and the sampled memory budget for the whole reviewed tree. An engine test pins the shared group; no Windows runner build exists yet, so the POSIX path is the supervised one.
- [src/VisoLoom.Plugin/CaptureCoordinator.cs](../../src/VisoLoom.Plugin/CaptureCoordinator.cs): a project-model pin restored from an opened `.3dm` is visible and preselected, but a cloud destination no longer renders on its own — the first render asks the user to choose the project model in the Model menu once per session. Local and managed pins keep restore-and-render, because they spend no external account. The wording of that status in the loaded panel remains a native acceptance case.

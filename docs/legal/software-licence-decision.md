# Software licence decision memo

**Executed 2026-11-21:** GPL-3.0-or-later is in force — the root `LICENSE` carries the canonical text, the CI guard was removed, [licensing status](licensing-status.md) and the README licence section were updated. The memo remains the record of the direction and the analysis behind the choice.

Written 2026-11-21 as a one-page decision record: the direction, the options assessed against it, and the recommendation.

## Direction (recorded 2026-11-21)

The direction, stated 2026-11-21: the source is **always open** — everyone can download and build it, for the foreseeable future and beyond. What may happen **later, maybe**, is selling **the compiled plug-in itself, possibly with support**; anyone who prefers to build from source is welcome to, no exclusivity over distribution is claimed, and no closed products are planned. The posture against closing software is philosophical. For contrast: the direction includes **no** dual licensing — selling a closed licence of the same code — and no open-core split into free and paid code.

## What the licence covers — and what it never touches

The licence governs **the repository source**: the C# plug-in, Providers and shared libraries, the reference server, the Python engine tools, tests and documentation.

It does not govern, and must stay visibly separate from: model weights (licensed by their publishers — Apache-2.0, FLUX Kommunity, and so on); the future bundled native runners, whose vendoring carries its own MIT-based artifact review ([engine productization plan](../development/engine-productization-plan.md), Milestones B/C); brand assets (managed separately under `assets/brand`); future Library assets and texture sets, which carry per-asset rights records per the [roadmap](../roadmap.md#lightweight-asset-browser); and host assemblies, which CI already refuses to ship — RhinoCommon/Eto are build references whose runtime Rhino supplies, an arrangement a GPL covers cleanly. The McNeel SDK/distribution-terms confirmation below is required **regardless of which licence is chosen**; compilation is not legal clearance.

## Options assessed

| Option | Fits the direction | Adoption friction (studios) | Keeps the software open downstream | Notes |
| --- | --- | --- | --- | --- |
| **GPL-3.0-or-later** | Yes — source stays open; selling compiled builds and support is fully permitted | Some corporate procurement policies avoid GPL-3 tools | Yes — every distributor must publish changes under the same terms | The first candidate raised, before this comparison |
| MIT | Yes, and others may equally sell builds of it | Lowest | No — anyone may close a modified version | The ecosystem's default; the downstream-openness guarantee the direction leans on is absent |
| Apache-2.0 | Yes, as MIT | Low | No — as MIT, plus explicit patent grant and notice procedures | Slightly more ceremony (NOTICE conventions) |
| AGPL-3 | Technically yes, but aimed at hosted services | High, and hostile to the office-server studios the roadmap pilots | Yes, extended to network use | The network clause solves a problem VisoLoom's desktop plug-in does not have |
| GPL-2-only | Yes | Higher, and dated | Yes | No explicit patent language, no Apache-2.0 compatibility; no reason to prefer it today |
| No licence (status quo) | Contradicts "always open" — legally all rights reserved | n/a | n/a | Interim state only; CI fails the bundled build deliberately |

## Recommendation

**GPL-3.0-or-later** — the first candidate raised, and the recommendation after assessment. It matches the stated direction exactly: the source stays open forever, packaged builds and support may be sold without asking anyone's permission (the GPL expressly permits charging any price for copies), every downstream distributor stays open (the philosophical point of the direction), and no exclusivity is claimed.

Three facts make it clean in this repository:

1. **The dependency inventory is entirely MIT/Apache-2.0/BSD** ([resolved dependencies](third-party-dependencies.md)); all are one-way compatible into a GPL-3 work, and the repo's own policy of not importing GPL/AGPL *dependencies* (ADR 0008) governs what VisoLoom imports, not what its own source is.
2. **The plug-in/host boundary is already enforced in CI**: no RhinoCommon, Rhino.UI, Eto or Ed.Eto assemblies ship in the package. A GPL-3 plug-in distributed against a proprietary host forces nothing on the host; this is the normal arrangement for GPL host-app plug-ins, and the McNeel terms confirmation remains its own mandatory step either way.
3. **Yak packages carry a `LICENSE.txt`** alongside the manifest (checked 2026-11-21 in the [package anatomy guide](https://developer.rhino3d.com/guides/yak/the-anatomy-of-a-package/)), so the licence text travels inside every `.yak` VisoLoom ever publishes.

The counterweight, stated honestly: permissive licences (MIT/Apache-2.0) maximise adoption, and parts of the Rhino ecosystem's paying audience are conservative procurement environments. If that ever outweighs the share-back guarantee, relaxing GPL-3-or-later to MIT is one commit away **while Pascal Nünninghoff remains the sole copyright holder**. The reverse is not: once outside contributions arrive, returning to GPL requires every contributor's consent. Choose the contributor-agreement approach (below) accordingly.

## Contributions

While the repository is single-author, relicensing in either direction is a one-person decision by the copyright holder. Before accepting outside contributions, pick one:

- **No agreement needed** if GPL-3-or-later is final forever — a short "by contributing you license under GPL-3.0-or-later" note in the pull-request template suffices.
- **A minimal CLA** ("you grant the project's copyright holder a perpetual, worldwide, non-exclusive licence to use and relicense your contribution") if flexibility to relax later matters. Sole-author status is the cheaper state to keep; a CLA is how you keep it.

## Name and resale

Further direction recorded 2026-11-21: the goal is people **building on** VisoLoom; others selling copies is accepted, a renamed resale presented as someone else's product is the unwanted scenario. The facts around it:

- **No open-source licence can forbid selling copies** — a licence that did would fail the open-source definition. GPL-3, like MIT, lets anyone charge any price.
- **A code licence never grants the name.** Trademark protection is a separate instrument: a reseller may redistribute, modify and rename the code, but cannot market it as "VisoLoom" or imply the project's endorsement — the same asymmetry by which anyone may sell the Red Hat family's GPL code while only Red Hat sells "Red Hat." The name is the part worth holding, not the code.
- **GPL-3 makes a resale maximally transparent.** The seller must convey source and licence, state their changes, and keep the work open, so their buyers are also the commons' downstream and improvements return; a MIT reseller could instead close a modified binary and sell it as an opaque product, which is strictly worse than the unwanted resale scenario.
- The stronger protections — source-available licences that forbid competing resale (the Elastic/Poly style) — are not open source and contradict the stated direction; GPL-3 plus holding the name is the honest maximum while staying open.

When the licence lands: carry a short note in the README and the Yak package that GPL-3.0-or-later covers the code while the VisoLoom name is not licensed. Registering the wordmark is a separate decision with costs per jurisdiction; this section is engineering guidance, not legal advice.

## Implementation checklist (executed 2026-11-21)

One commit: the chosen `LICENSE` text at the repository root; remove the `Project licensing remains undecided` guard from the Windows package job in `.github/workflows/build.yml`; update [licensing-status.md](licensing-status.md) from "undecided" to the decision with its date; and note the decision in [third-party dependencies](third-party-dependencies.md) ("VisoLoom's own licence is GPL-3.0-or-later; the dependency review is unchanged"), plus the README name note above. GPL-3 requires no NOTICE file (that is an Apache convention); carry the licence text into the Yak `LICENSE.txt` when packaging begins. The CI guard exists precisely so this decision cannot slip in undocumented.

## Gates that remain open regardless of the licence

McNeel SDK/distribution-terms confirmation before any binary release; the per-artifact runtime redistribution review before native runners ship ([engine plan](../development/engine-productization-plan.md), Milestone B); no Yak package, packaged binary release or other distribution without an explicit go-ahead; and per-asset rights records for anything the Library ever distributes. None of these is resolved by choosing a licence; all of them precede distribution in [ADR 0022's release sequence](../adr/0022-engine-bundling-and-one-click-model-install.md#proposed-release-sequence).

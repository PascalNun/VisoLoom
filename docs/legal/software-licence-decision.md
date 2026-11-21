# Software licence decision memo

Written 2026-11-21 to give the owner a decision he can make from one page. The [repository status](licensing-status.md) is unchanged by this memo: the GitHub repository is private, there is no `LICENSE` file, and CI deliberately fails the packaged build while one is absent (`.github/workflows/build.yml`). This memo records the owner's direction, assesses the options against it, and names the recommendation; **the decision is complete only when the owner adds the licence.**

## Owner direction (recorded 2026-11-21)

From the owner's own words in the working session, substantively: the source is **always open** — everyone can download and build it, for the foreseeable future and beyond. What may happen **later, maybe**, is selling **the compiled plug-in itself, possibly with support**, and anyone who prefers to build from source is welcome to; he claims no exclusivity over distribution and does not plan closed products. His philosophical lean is against closing software. (For contrast, he does **not** describe dual licensing — selling a closed licence of the same code — nor an open-core split into free and paid code.)

## What the licence covers — and what it never touches

The licence governs **the repository source**: the C# plug-in, Providers and shared libraries, the reference server, the Python engine tools, tests and documentation.

It does not govern, and must stay visibly separate from: model weights (licensed by their publishers — Apache-2.0, FLUX Kommunity, and so on); the future bundled native runners, whose vendoring carries its own MIT-based artifact review ([engine productization plan](../development/engine-productization-plan.md), Milestones B/C); brand assets (managed separately under `assets/brand`); future Library assets and texture sets, which carry per-asset rights records per the [roadmap](../roadmap.md#lightweight-asset-browser); and host assemblies, which CI already refuses to ship — RhinoCommon/Eto are build references whose runtime Rhino supplies, an arrangement a GPL covers cleanly. The McNeel SDK/distribution-terms confirmation below is required **regardless of which licence is chosen**; compilation is not legal clearance.

## Options assessed

| Option | Fits the owner's direction | Adoption friction (studios) | Keeps the software open downstream | Notes |
| --- | --- | --- | --- | --- |
| **GPL-3.0-or-later** | Yes — source stays open; selling compiled builds and support is fully permitted | Some corporate procurement policies avoid GPL-3 tools | Yes — every distributor must publish changes under the same terms | What the owner himself floated first |
| MIT | Yes, and others may equally sell builds of it | Lowest | No — anyone may close a modified version | The ecosystem's default; the fork-protection the owner philosophically leans toward is absent |
| Apache-2.0 | Yes, as MIT | Low | No — as MIT, plus explicit patent grant and notice procedures | Slightly more ceremony (NOTICE conventions) |
| AGPL-3 | Technically yes, but aimed at hosted services | High, and hostile to the office-server studios the roadmap pilots | Yes, extended to network use | The network clause solves a problem VisoLoom's desktop plug-in does not have |
| GPL-2-only | Yes | Higher, and dated | Yes | No explicit patent language, no Apache-2.0 compatibility; no reason to prefer it today |
| No licence (status quo) | Contradicts "always open" — legally all rights reserved | n/a | n/a | Interim state only; CI fails the bundled build deliberately |

## Recommendation

**GPL-3.0-or-later**, as the owner himself proposed. It matches his stated direction exactly: the source is open forever, he may sell packaged builds and support without asking anyone's permission (the GPL expressly permits charging any price for copies), every downstream distributor stays open (the philosophical point he made), and no exclusivity is claimed.

Three facts make it clean in this repository:

1. **The dependency inventory is entirely MIT/Apache-2.0/BSD** ([resolved dependencies](third-party-dependencies.md)); all are one-way compatible into a GPL-3 work, and the repo's own policy of not importing GPL/AGPL *dependencies* (ADR 0008) governs what VisoLoom imports, not what its own source is.
2. **The plug-in/host boundary is already enforced in CI**: no RhinoCommon, Rhino.UI, Eto or Ed.Eto assemblies ship in the package. A GPL-3 plug-in distributed against a proprietary host forces nothing on the host; this is the normal arrangement for GPL host-app plug-ins, and the McNeel terms confirmation remains its own mandatory step either way.
3. **Yak packages carry a `LICENSE.txt`** alongside the manifest (checked 2026-11-21 in the [package anatomy guide](https://developer.rhino3d.com/guides/yak/the-anatomy-of-a-package/)), so the licence text travels inside every `.yak` VisoLoom ever publishes.

The counterweight, stated honestly: permissive licences (MIT/Apache-2.0) maximise adoption, and parts of the Rhino ecosystem's paying audience are conservative procurement environments. If that ever outweighs the share-back guarantee, relaxing GPL-3-or-later to MIT is one commit away **while the owner remains the sole copyright holder**. The reverse is not: once outside contributions arrive, returning to GPL requires every contributor's consent. Choose the contributor-agreement approach (below) accordingly.

## Contributions

While the owner is the sole author, relicensing in either direction stays his prerogative. Before accepting outside contributions, pick one:

- **No agreement needed** if GPL-3-or-later is final forever — a short "by contributing you license under GPL-3.0-or-later" note in the pull-request template suffices.
- **A minimal CLA** ("you grant [owner] a perpetual, worldwide, non-exclusive licence to use and relicense your contribution") if flexibility to relax later matters. Sole-author status is the cheaper state to keep; a CLA is how you keep it.

## Implementation, when the owner decides

One commit, when explicitly instructed: add the chosen `LICENSE` text at the repository root; remove the `Project licensing remains undecided` guard from the Windows package job in `.github/workflows/build.yml`; update [licensing-status.md](licensing-status.md) from "undecided" to the decision with its date; and note the decision in [third-party dependencies](third-party-dependencies.md) ("VisoLoom's own licence is GPL-3.0-or-later; the dependency review is unchanged"). GPL-3 requires no NOTICE file (that is an Apache convention); carry the licence text into the Yak `LICENSE.txt` when packaging begins. The CI guard exists precisely so this decision cannot slip in undocumented.

## Gates that remain open regardless of the licence

McNeel SDK/distribution-terms confirmation before any binary release; the per-artifact runtime redistribution review before native runners ship ([engine plan](../development/engine-productization-plan.md), Milestone B); no Yak package or public visibility change without explicit instruction; and per-asset rights records for anything the Library ever distributes. None of these is resolved by choosing a licence; all of them precede distribution in [ADR 0022's release sequence](../adr/0022-engine-bundling-and-one-click-model-install.md#proposed-release-sequence).

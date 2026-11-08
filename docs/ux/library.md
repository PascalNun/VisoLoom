# Future VisoLoom Library

Planning only. Render and its capture, fidelity and review workflow remain the current priority. The Library concept image illustrates browsing, previewing and inserting resources. Its counts, grid, shortcuts and navigation are illustrative.

**Model in Rhino → Prepare with native assets and materials → Represent with VisoLoom.** The Library helps architects prepare the source that Rhino captures. Once placed or imported, the resources stay editable and usable in Rhino without VisoLoom or an image model.

## Browse and use resources

| Area | What to show | What happens in Rhino |
| --- | --- | --- |
| **Assets** | Search and categories for planting, people, furniture, vehicles and other architectural context. Show a thumbnail from the actual Rhino asset, dimensions and useful variants. | **Insert into Model** places ordinary Rhino geometry, preferably as a reusable block, using native placement. Respect document units and a useful insertion point. Reuse a compatible definition and never silently replace an existing block. |
| **Materials** | Search and a sample preview that shows the surface character and typical mapping scale. Include usable textures, colours and material properties. | **Add to Rhino** imports an ordinary render material. Rhino's Materials panel handles assignment and editing. Consider a native assignment shortcut only if it respects Rhino's selection, mapping and block inheritance. Reuse matching content where practical; never silently replace an assignment. |

Browse and preview without changing the document. Insertion, import and assignment are explicit actions on the intended Rhino document and must be undoable. Use [Rhino blocks and Insert](https://docs.mcneel.com/rhino/8/help/en-us/commands/block.htm) and [Rhino material import and assignment](https://docs.mcneel.com/rhino/8mac/help/en-us/commands/materials.htm) as the starting points. A conflicting name, unavailable texture or cancelled placement should have a clear outcome without leaving a partially imported resource.

Curate, author or generate assets to communicate their form and material in Rhino's own view. Optimise for everyday modelling while retaining the details the captured image needs, such as canopy shape, fine branching or furniture silhouettes. Evaluate detail and performance in a representative scene rather than imposing an extremely low polygon budget. Material previews should reflect actual Rhino appearances and preserve useful texture scale and mapping. Validate that placed assets and textures survive save and reopen, including on a different machine without VisoLoom. See the [content and rights roadmap](../roadmap.md#lightweight-asset-browser).

## Where the Library belongs

| Option | Benefit | What to test |
| --- | --- | --- |
| **Render / Library within VisoLoom** | One place to discover both workflows, with one panel taking up viewport space. Assets and Materials sit within Library. | Can users place several items and return to their render without losing context? Do nested choices stay clear in a narrow dock? |
| **Separate dockable Library panel** | Users can keep resources visible while modelling or comparing a render, and close the Library when they are finished. | Is the panel easy to find? Does a second dock take too much space? Does placement target the right document when switching windows or inspectors? |

Keep both options open. When there is a small usable collection, compare the same insert, assign and return-to-render tasks on Mac and Windows. Try a compact search/filter and list with on-demand details at narrow widths, and a thumbnail grid with details at wider widths. Use [Rhino's native dockable Eto panels](https://developer.rhino3d.com/guides/rhinocommon/tabbed-panels/) and test document and inspector lifetimes before choosing the host.

## Boundary with Render

Render remains the primary workspace. Keep future browsing and placement separate from capture, providers and result history so either panel arrangement can reuse the same Library view. Moving between Render and Library or browsing resources must preserve Render settings and results without invalidating an otherwise current preview. An actual scene edit requires a fresh preview for the next render; existing jobs and comparisons retain their original capture. Library use requires no rendering credentials, starts no generation, and adds no automatic upload of geometry, texture files or catalogue metadata.

Defer Render / Library navigation until the Library offers a real workflow. This plan adds no panel, placeholder, browser, resource database, backend, dependency or infrastructure. The existing model catalogue in Settings continues to manage image model setup, separate from this proposed collection of Rhino resources.

# Rhino-native interaction rules

Status: accepted UX, with official native interaction references checked 2026-09-19.

Rhino's Named Views panel already saves/restores views and can restore their aspect ratio. VisoLoom should read the active viewport after the architect makes that choice. Do not mirror the Named Views list into a new editable camera manager. [Rhino NamedView help](https://docs.mcneel.com/rhino/8/help/en-us/commands/namedview.htm).

Use Eto in a registered document-associated Rhino panel. Native panels have different lifetime behavior across Windows and Mac inspectors, so jobs/history must exist independently of any panel object. [Rhino tabbed panels](https://developer.rhino3d.com/guides/rhinocommon/tabbed-panels/).

| Native concept | VisoLoom behavior | Duplication to avoid |
| --- | --- | --- |
| Active viewport and projection | Read and capture; show name/projection as status. | Camera position, lens, orbit or aspect manager. |
| Named Views | Browse Rhino's entries with local composition thumbnails; restore only through explicit Show. | Separate saved views or a second camera editor. |
| Display modes | Default capture source is the current mode. | Another realtime renderer/display workspace. |
| Layers and visibility | Read effective Rhino state and disclose incomplete extraction. | Separate layer visibility tree. |
| Materials | Read assigned identity/appearance; optional additional description. Planned library content imports as ordinary editable Rhino materials through an explicit user action. | A second material editor or capture-time material reassignment. |
| Sun and scene lighting | Use the visible Rhino lighting as reference; future provenance records useful supported settings. | A separate sun, location or lighting editor, or an automatic lighting replacement. |
| Clipping planes | Honor enabled view-specific clipping. | Another section-plane editor. |
| Selections | Preserve Rhino selection. Future object editing starts from Rhino selection. | New pick/navigation mode for the same model. |
| Units | Read from the document and retain in metadata. | A second project-units setting. |
| Command line | `VisoLoom` opens panel; `VisoLoomRender` uses the same action. | Overriding Rhino Render or requiring many commands for ordinary use. |

Use native dialogs for file saving and connections where appropriate, familiar labels, accessible focus order, and restrained notification behavior. Result images may have their own inspection zoom; this does not become model navigation.

No automatic view restoration on completion, no global document lookup after an await, no model mutations to recolor objects for passes, and no image assets embedded into `.3dm` by default. If a capture technique requires temporary host changes, prove restoration and absence of undo/dirty-state side effects before acceptance; otherwise choose a different technique or report it unsupported.

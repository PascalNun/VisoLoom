# VisoLoom

Generative Representation for Rhino

VisoLoom helps architects represent the design they already made in Rhino. Rhino remains the authority for geometry, views, materials, visibility and sections. Geometry preservation is an engineering objective across every representation mode; generated images still require comparison with the design.

**Development software.** OpenAI editing and image export have been tested in Rhino on Mac. The first local FLUX.2 klein 4B engine also completes real edits without Draw Things. Its Settings and panel connection are implemented; the loaded Rhino workflow still needs a native check. Broader quality and compatibility checks remain.

VisoLoom targets the **Rhino 8 series on Mac and Windows**, using Rhino's default .NET runtime. The first slice builds against the original Rhino 8 SDK; no later service release is required by its APIs. Individual releases still need native testing before we claim verified compatibility. See the [compatibility decision](docs/adr/0010-rhino-8-series-compatibility.md).

Choose **This computer · capture test** for an unchanged reference, or configure **OpenAI** in Settings for image editing. The panel lists Rhino's Named Views first, alongside the active and open viewports. **Show named view in Rhino** explicitly restores the chosen saved view; Render then captures its visible frame. Custom sizes keep width and height linked and show any model padding or resizing. Model choice, representation preset and optional prompt stay in the panel. Preview captures the Rhino view locally. Render submits that reviewed image and intent, then returns an image for comparison and PNG export. OpenAI receives one image and bounded scene context only after you enable the connection and click **Render**.

With a prepared local engine, use **Settings → Check local model**, then choose **FLUX.2 klein 4B · this computer** in the Model menu. It uses the reference image and a short editing prompt, without an API key. Model preparation is still a developer setup; Windows GPU inference and automatic installation remain planned.

Settings also includes **Existing model folders → Find existing model…**. It checks a folder's layout and remembers possible candidates without copying weights or changing the active model. Draw Things checkpoints, missing components and unsupported formats get an explanation. A saved folder still needs preparation and verification before it can render; this is the first part of model reuse, not an automatic importer.

[Build and run](DEVELOPMENT.md) · [Current review](docs/development/phase-4-review.md) · [Roadmap](docs/roadmap.md) · [Working on the code](CONTRIBUTING.md)

**Try the latest code on your Mac:** double-click [Test VisoLoom.command](Test%20VisoLoom.command). It builds the plug-in, prepares the local test service and opens Rhino. Restart Rhino after a changed build, then run `VisoLoom` and use **Test capture**. The [quick-test steps](DEVELOPMENT.md#quick-test-on-your-mac) explain the automatic local connection.

## Find your way around

| Folder | What lives here |
| --- | --- |
| `src/VisoLoom.Plugin` | Rhino capture, the native panel and local image history |
| `src/VisoLoom.Core` | Scene information, framing, representation intent and provider capabilities |
| `src/VisoLoom.Providers` | Local image editing, capture testing and OpenAI; other providers are deferred |
| `src/VisoLoom.Protocol` | Messages and validation for the local service |
| `server/VisoLoom.ReferenceServer` | Authenticated local jobs for capture testing and a prepared editing engine |
| `engine` | Separate local model worker and verified weight preparation |
| `tests` | Checks for framing, scene data, local transport and OpenAI request handling |
| `examples` | Editable representation presets and an authored sample Scene Package |
| `assets/brand` | Original logo sources and the supplied brand reference board |
| `docs` | Architecture decisions, UX, implementation notes and dependency records |

To change a workflow, start with the panel and capture coordinator. To change framing or scene rules, start in Core. Preset text is ordinary JSON in `examples/presets`. Keep changes near their purpose; new folders or frameworks should earn their place.

The planned [asset browser](docs/roadmap.md#lightweight-asset-browser) follows the same principle: place optimized, recognizable plants and other context directly in Rhino, then use their descriptions to guide image-model refinement. The assets should already look close to the intended result in Rhino. Placement, scale and variation stay with the designer.

## Further reading

- [Accepted architecture](docs/architecture/proposed-architecture.md) and [decisions](docs/adr/README.md)
- [Product and UX principles](docs/ux/product-principles.md) and [Rhino acceptance checklist](docs/development/plugin-smoke-test.md)
- [Scene Package](docs/scene-package/specification.md), [JSON Schema](docs/scene-package/scene-1.0.schema.json) and [local service protocol](docs/architecture/local-server-protocol.md)
- [Brand resources](assets/brand/README.md)
- [Dependencies](docs/legal/third-party-dependencies.md) and [licensing status](docs/legal/licensing-status.md)

The [VisoLoom repository](https://github.com/PascalNun/VisoLoom) is private. Licensing remains undecided; there is no project LICENSE file. BFL cloud, Google and xAI integrations remain deferred. OpenAI and local FLUX edits have real Mac evidence; architectural fidelity still needs systematic evaluation. No public release or Yak publication has been created.

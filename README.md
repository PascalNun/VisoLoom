# VisoLoom

Generative Representation for Rhino

VisoLoom helps architects represent the design they already made in Rhino. Rhino remains the authority for geometry, views, materials, visibility and sections. Geometry preservation is an engineering objective across every representation mode; future generated images will still require comparison with the design.

**Development capture-test slice implemented.** The solution builds and the local HTTP round trip is tested. The native Rhino panel and capture code compile, but have not yet been exercised in a supported Rhino host. This is not a validated plug-in release.

VisoLoom targets the **Rhino 8 series on Mac and Windows**, using Rhino's default .NET runtime. The first slice builds against the original Rhino 8 SDK; no later service release is required by its APIs. Individual releases still need native testing before we claim verified compatibility. See the [compatibility decision](docs/adr/0010-rhino-8-series-compatibility.md).

The current workflow is **Rhino view → Scene Package → authenticated local job → unchanged reference PNG → source/result preview and Save**. The panel deliberately says **Test capture**. Modes and prompts are disabled because no image-generation provider is connected.

[Build and run](DEVELOPMENT.md) · [Current review](docs/development/phase-4-review.md) · [Roadmap](docs/roadmap.md) · [Working on the code](CONTRIBUTING.md)

**Try the latest code on your Mac:** double-click [Test VisoLoom.command](Test%20VisoLoom.command). It builds the plug-in, prepares the local test service and opens Rhino. Restart Rhino after a changed build, then run `VisoLoom` and use **Test capture**. The [quick-test steps](DEVELOPMENT.md#quick-test-on-your-mac) explain the one-time connection.

## Find your way around

| Folder | What lives here |
| --- | --- |
| `src/VisoLoom.Plugin` | Rhino capture, the native panel and local image history |
| `src/VisoLoom.Core` | Scene information, framing, representation intent and provider capabilities |
| `src/VisoLoom.Providers` | Image-service connections; the local adapter works, cloud integrations are deferred |
| `src/VisoLoom.Protocol` | Messages and validation for the local service |
| `server/VisoLoom.ReferenceServer` | A small development service that returns the captured image unchanged |
| `tests` | Checks for framing, scene data and the local round trip |
| `examples` | Editable representation presets and an authored sample Scene Package |
| `docs` | Architecture decisions, UX, implementation notes and dependency records |

To change a workflow, start with the panel and capture coordinator. To change framing or scene rules, start in Core. Preset text is ordinary JSON in `examples/presets`. Keep changes near their purpose; new folders or frameworks should earn their place.

The planned [asset browser](docs/roadmap.md#lightweight-asset-browser) follows the same principle: place optimized, recognizable plants and other context directly in Rhino, then use their descriptions to guide image-model refinement. The assets should already look close to the intended result in Rhino. Placement, scale and variation stay with the designer.

## Further reading

- [Accepted architecture](docs/architecture/proposed-architecture.md) and [decisions](docs/adr/README.md)
- [Product and UX principles](docs/ux/product-principles.md) and [Rhino acceptance checklist](docs/development/plugin-smoke-test.md)
- [Scene Package](docs/scene-package/specification.md), [JSON Schema](docs/scene-package/scene-1.0.schema.json) and [local service protocol](docs/architecture/local-server-protocol.md)
- [Dependencies](docs/legal/third-party-dependencies.md) and [licensing status](docs/legal/licensing-status.md)

The [VisoLoom repository](https://github.com/PascalNun/VisoLoom) is private. Licensing remains undecided; there is no project LICENSE file. Cloud providers are unavailable descriptors only. No paid API requests, inference models, public releases or Yak publication are part of this milestone.

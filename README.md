# VisoLoom

**Represent the design you already made in Rhino.**

VisoLoom turns the model you composed in Rhino into the image you need — without redrawing it. Capture the view you set up, choose a representation, and let an image model render that same design: a photorealistic study, a sketch, or any mode in between. VisoLoom is built for architects and designers whose model already says what the design is.

Rhino stays in charge. Your geometry, views, layers, materials, sections and Named Views are read and captured — never redrawn by the image. **Geometry preservation is the core promise:** every result can be compared with its Rhino capture at the same zoom, so you see exactly what the model changed before anything is saved. Preservation is checked, never assumed.

## How a render works

1. Compose the view in Rhino — the current viewport, a Named View, or your own display style with edges and ambient occlusion.
2. In the VisoLoom panel, choose the view, a representation preset, the model, an optional prompt and the output size. Custom sizes keep width and height linked to your camera.
3. **Preview** freezes the exact image that will be sent. **Render** sends only that reviewed image and your intent — never your file, never more than you approved.
4. Compare the result with its source, then **Save PNG…**. Finished images save automatically to VisoLoom's local image folder, and you can add one more output folder for the project.

## Where the render runs

| What you need | Destination | Status today |
| --- | --- | --- |
| Cloud image editing with your own key | **OpenAI** in Settings | Working — the models your account lists, rendered, compared and saved in one panel |
| The same editing through other clouds | **Google** and **xAI** in Settings | Wired for the same account-listed choice; first live edits still owed |
| FLUX as a cloud render | **BFL** in Settings | Wired; rendering stays off until its open reference decision lands |
| Rendering entirely on your own machine | Local **FLUX.2 klein 4B** | Early — real edits complete; setup is still a developer task while one-click install is in progress |
| A model folder you already have | **Find existing model…** in Settings | Working — VisoLoom explains what it finds instead of guessing |

For each destination the panel names what will receive your image before you render, and Settings keeps every credential in the operating system's key store, never in the file.

## What VisoLoom sends

The frozen image and your intent — plus, for cloud destinations, a bounded summary of the scene: the camera, frame, units, clipping and material colours as suggestions. Layer and material names are optional. Nothing leaves your machine until you enable the destination and press **Render**, and a job keeps its captured view even while you keep modelling.

## Try it today

**VisoLoom has not been released as a package yet.** Today the way to run it is from this repository: [DEVELOPMENT.md](DEVELOPMENT.md) walks through building the plug-in, installing the development copy for Rhino 8 and starting the local service — one double-click on the Mac, documented build-and-load steps on Windows. Pulling the repository alone never changes what is inside Rhino; the launcher does.

VisoLoom targets the **Rhino 8 series on Mac and Windows** with Rhino's default .NET runtime, and no later service release is required by its APIs; compatibility is confirmed release by release. Windows installation is not packaged yet. The [compatibility decision](docs/adr/0010-rhino-8-series-compatibility.md) records the details.

## Documentation

| Start here | Go deeper |
| --- | --- |
| [Product principles](docs/ux/product-principles.md) — the promises the product makes | [Accepted decisions](docs/adr/README.md) and [architecture](docs/architecture/proposed-architecture.md) |
| [User journeys](docs/ux/user-journeys.md) and [panel notes](docs/ux/panel-wireframe.md) | [Scene Package](docs/scene-package/specification.md) and the [local service protocol](docs/architecture/local-server-protocol.md) |
| [Roadmap](docs/roadmap.md) — where the work goes next | [Engine productization](docs/development/engine-productization-plan.md) · [model selection](docs/development/model-selection-plan.md) · [framing](docs/development/framing-safe-frame-plan.md) · [Library assets](docs/development/library-asset-production-plan.md) |
| [Build and run](DEVELOPMENT.md) | [Contributing](CONTRIBUTING.md) and the [current review](docs/development/phase-4-review.md) |

The planned [Library](docs/ux/library.md) follows the same principle as the render workflow: plants and context assets are placed and edited in Rhino itself, already close to the intended result, and the image refines what is visibly there.

## Licence

VisoLoom's source is licensed under **GPL-3.0-or-later** — see [LICENSE](LICENSE) and the [licence status](docs/legal/licensing-status.md); the analysis behind the choice is in the [decision memo](docs/legal/software-licence-decision.md). The GPL covers the code: anyone may use, study, modify and redistribute it, including commercially, with distributed changes published under the same terms. The **VisoLoom name is not licensed by the GPL** — and neither are the brand identity assets in `assets/brand`, which stay with their creator. Derivative distributions carry their own name and may not imply endorsement. Bundled-library notices will be recorded before any binary distribution.

The [VisoLoom repository](https://github.com/PascalNun/VisoLoom) is public under GPL-3.0-or-later. Architectural fidelity, native acceptance and packaging remain open work — recorded honestly in the [current review](docs/development/phase-4-review.md). No packaged release or Yak publication exists yet.

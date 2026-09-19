# VisoLoom

Generative Representation for Rhino

VisoLoom is a proposed representation layer for architects and designers working in Rhino. Rhino remains the authority for geometry, views, materials, visibility, and sections.

**Status: Phase 0 review completed; architecture proposed, awaiting confirmation.** This workspace contains research and design documentation. No plug-in, inference service, cloud adapter, or build pipeline has been implemented or tested. No GitHub repository has been created yet.

Start with the [technical review](docs/architecture/technical-review.md), then the [proposed architecture](docs/architecture/proposed-architecture.md) and [panel wireframes](docs/ux/panel-wireframe.md).

| Review area | Documents |
| --- | --- |
| Rhino feasibility | [Evidence and outstanding experiments](docs/development/rhino-feasibility.md) |
| Providers | [Dated capability research](docs/providers/current-capabilities.md), [capability contract](docs/providers/capability-model.md) |
| Contracts | [Scene Package](docs/scene-package/specification.md), [HTTP protocol](docs/architecture/local-server-protocol.md), [resolution](docs/architecture/resolution-negotiation.md) |
| Domain behavior | [Prompts, mappings, jobs, and results](docs/architecture/domain-model.md) |
| Decisions | [Proposed ADRs](docs/adr/README.md) |
| UX | [Principles](docs/ux/product-principles.md), [journeys](docs/ux/user-journeys.md), [information architecture](docs/ux/information-architecture.md), [Rhino integration](docs/ux/rhino-native-principles.md) |
| Development | [Environment and delivery plan](DEVELOPMENT.md), [contributing](CONTRIBUTING.md) |
| Licensing | [Undecided status](docs/legal/licensing-status.md), [dependency review](docs/legal/third-party-dependencies.md) |

The next authorized stage, after architecture confirmation, is a private GitHub repository named **VisoLoom**, a buildable C# solution, and a reference-capture round trip. A passthrough image proves transport and capture; it does not prove generative representation quality.

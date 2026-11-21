# Contributing to VisoLoom

The project has an accepted architecture and a development capture-test implementation. Its source is licensed GPL-3.0-or-later and the repository is private. Do not infer permission to publish project source, assets, binaries or confidential scene fixtures beyond the licence's terms.

Start with the [repository map](README.md#find-your-way-around) and [quick local test](DEVELOPMENT.md#quick-test-on-your-mac). Read the [product principles](docs/ux/product-principles.md) when changing a workflow, and the relevant [architecture decision](docs/adr/README.md) when changing a boundary. You do not need to read every design document to edit a preset or fix a panel label. ADRs 0001–0008 are accepted; 0009 records the delivered scope and 0010 updates Rhino 8 compatibility. Distinguish implemented behavior from pending host validation.

Keep Rhino extraction in the plug-in, neutral planning in Core, wire DTOs in Protocol, and API-specific requests in adapters. The panel must not query a cloud API directly. Avoid duplicate camera/material state and repeated domain logic.

Write code a person can follow without reconstructing hidden assumptions. Use descriptive names, one operation per line, short cohesive methods and ordinary control flow. Keep simple properties compact. Comments should explain constraints or reasons; avoid narrating the code or adding boilerplate. The shared `.editorconfig` defines spacing. Prefer direct composition over speculative frameworks, and keep related files together in the existing projects.

Use English for code and documentation. Explain visible behavior in the language of architects and designers: view, section, material, resolution and representation. Keep the normal panel compact and use Rhino's existing tools. Do not expose a setting unless it supports a meaningful choice. Reuse proven infrastructure for HTTP, cryptography, credentials, serialization and codecs.

Test consequential behavior with independent examples: framing, material precedence, capability/policy negotiation, version compatibility, prompt composition, job transitions and protocol failure recovery. Record what was built/tested and what still requires a Rhino host or a real provider.

Every new dependency needs a purpose, exact version, transitive/license review and distribution record in [third-party-dependencies.md](docs/legal/third-party-dependencies.md). GPL/AGPL or similarly consequential dependencies require explicit approval under the project brief. Do not add secrets, raw project geometry, customer screenshots, scene packages, generated results or local absolute-path configuration to Git.

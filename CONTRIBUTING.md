# Contributing to VisoLoom

The project is currently in architecture review. Its licensing model is undecided and the future repository must initially be private. Do not infer permission to publish project source, assets, binaries or confidential scene fixtures.

Read the [technical review](docs/architecture/technical-review.md), [ADRs](docs/adr/README.md), [product principles](docs/ux/product-principles.md), and [development status](DEVELOPMENT.md) before substantial code changes. Proposed records are not accepted decisions until the architecture review is confirmed.

Keep Rhino extraction in the plug-in, neutral planning in Core, wire DTOs in Protocol, and API-specific requests in adapters. The panel must not query a cloud API directly. Avoid duplicate camera/material state and repeated domain logic.

Use clear English technical identifiers and documentation, concise architectural UI language, explicit failure states, small cohesive components and comments explaining non-obvious constraints. Prefer direct composition over speculative frameworks. Reuse proven infrastructure for HTTP, cryptography, credentials, serialization and codecs.

Test consequential behavior with independent examples: framing, material precedence, capability/policy negotiation, version compatibility, prompt composition, job transitions and protocol failure recovery. Record what was built/tested and what still requires a Rhino host or a real provider.

Every new dependency needs a purpose, exact version, transitive/license review and distribution record in [third-party-dependencies.md](docs/legal/third-party-dependencies.md). GPL/AGPL or similarly consequential dependencies require explicit approval under the project brief. Do not add secrets, raw project geometry, customer screenshots, scene packages, generated results or local absolute-path configuration to Git.

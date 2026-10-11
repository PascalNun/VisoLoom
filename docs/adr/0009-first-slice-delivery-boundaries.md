# ADR 0009 — First-slice delivery boundaries

Status: implementation refinement of the authorized reference vertical slice, 2026-10-11.

The initial SDK/runtime baseline in the first paragraph below is superseded by [ADR 0010](0010-rhino-8-series-compatibility.md). The other delivery boundaries remain in force.

## Decision

Pin stable RhinoCommon 8.21.25188.17001 and declare minimum Rhino 8.21 with .NET 8, refining the accepted 8.20+ baseline because NuGet's 8.20 series contains only prereleases. Compilation and native host acceptance are separate gates. The installed Rhino 8.16 is not modified.

Implement authenticated literal-loopback passthrough first. Require a session token supplied through the server environment and panel; defer Keychain/Credential Manager persistence, office HTTPS and cloud execution. Provide the credential abstraction and an explicit session-only implementation now. This fulfills the reference slice without pretending untested OS storage or provider integrations are operational.

Keep the generated-image mode and prompt disabled and use **Test capture**. Initially reject page/detail, two-point perspective and display modes beyond Wireframe/Shaded/Rendered. The current output is byte-identical input, not a geometry-preserving model evaluation. Auxiliary passes remain unsupported declarations; exact-ratio resolution planning is implemented, but no resampling adapter is enabled.

## Consequences

The source, build and local HTTP round trip are reviewable now. End-to-end Rhino acceptance, UX observation, richer material/instance semantics, OS credential tests and model fidelity benchmarks remain release gates. No public package or licensing choice follows from the scaffold. See the [implementation review](../development/phase-4-review.md).

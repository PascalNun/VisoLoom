# ADR 0008 — Dependency discipline and optional inference

Status: Accepted by the user on 2026-09-19.

## Context

The project should remain maintainable and lightweight without constraining its undecided distribution model. Heavy ML dependencies and casual native image packages enlarge the Rhino compatibility surface.

## Decision

Use platform JSON, HTTP, hashing, file streaming and proven codecs. Core has no host/UI/ML references. Do not install provider SDKs solely for one simple HTTP call unless their maintenance/authentication behavior justifies them. Use host-supplied Rhino/Eto assemblies without redistributing duplicate copies. Keep inference applications and weights outside Rhino.

Use an established test framework after recording exact licenses/versions. Pin direct/transitive versions during scaffold and review redistribution notices. Do not add GPL/AGPL or comparably consequential dependencies without the explicit approval required by the brief. Evaluate conditional licenses individually.

## Alternatives and consequences

Large DI, workflow and image frameworks are not justified by a passthrough slice. Handwritten crypto, HTTP parsing, OAuth or codecs would save packages at the cost of correctness. ASP.NET Core's built-in services are acceptable in the optional server; no comparable framework belongs in the plug-in.

No package has been added in Phase 0. Revisit only for demonstrated requirements or measured performance. Candidate inventory: [third-party dependencies](../legal/third-party-dependencies.md).

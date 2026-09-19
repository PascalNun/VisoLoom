# ADR 0007 — Compact native UI and honest fidelity

Status: Accepted by the user on 2026-09-19.

## Context

Architects already set views, materials and sections in Rhino. The proposed primary engine/model and fidelity controls risk adding repeated decisions or implying false precision.

## Decision

Use native Eto controls: read-only view, Mode, optional Prompt, prominent Resolution, visible destination/model row, Render, and compact result/progress area. Put implementation diagnostics and genuine provider-specific parameters in Advanced. Use one shared action for panel and command execution.

Remove a continuous Geometry Fidelity slider initially. Geometry preservation remains a core objective across modes, pursued through preset instructions and useful supported controls. Provide source comparison and treat architectural deviations as defects to reduce; do not advertise a deterministic generative guarantee. The development reference engine uses Test capture and disables style inputs.

## Alternatives and consequences

A browser/chat interface adds interaction and rendering dependencies without a concrete need. A second material/view manager violates the core workflow. Keeping every future setting visible increases setup burden.

Requires early usability tests at realistic panel widths, keyboard use and both OS themes. Revisit fidelity controls only when a model/pipeline provides interpretable behavior. Wireframes and justifications: [UX information architecture](../ux/information-architecture.md).

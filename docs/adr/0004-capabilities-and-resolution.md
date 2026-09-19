# ADR 0004 — Capabilities and resolution planning

Status: Accepted by the user on 2026-09-19.

## Context

Provider family names do not determine current model inputs, resolution, mask semantics or cancellation. Treating every auxiliary image as a control is incorrect. Cropping to a provider ratio can remove designed architecture.

## Decision

Version capabilities by model/endpoint/adapter and evidence date. Declare each pass's actual usage. Core intersects capabilities, pass availability, user requirements and policy, then computes a single explicit resolution plan. Preserve Rhino's frame through exact dimensions or declared uniform scaling/padding and exact recovery. Validate actual outputs.

Adapters translate this plan to their APIs and reject stale/unsupported plans. Unimplemented adapters fail before network access. Image count, seed acceptance, repeatability, progress and remote cancellation are separate properties.

## Alternatives

Provider conditions in UI duplicate logic. A universal strict/creative numeric scale suggests nonexistent equivalence. Automatic cropping simplifies requests at the expense of the core composition requirement.

## Consequences and revisit

Capability snapshots and constraints need maintenance and tests. Padding protects the submitted frame but cannot guarantee model composition. Resampling must be visible and never labelled native generation. Revisit controls after measured architectural benchmarks. Drafts: [capabilities](../providers/capability-model.md), [resolution](../architecture/resolution-negotiation.md).

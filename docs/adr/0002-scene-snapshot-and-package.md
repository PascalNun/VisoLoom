# ADR 0002 — Immutable scene and external package

Status: Accepted by the user on 2026-09-19.

## Context

Rhino state can change while a request is queued. Providers need different subsets of scene data. Passing live Rhino objects to adapters would couple providers to the host and invalidate provenance.

## Decision

Create one immutable snapshot at submission using the explicit Rhino document/viewport. Store versioned UTF-8 JSON plus PNG assets in private application data. Every raster uses the same capture frame and camera. Required reference, optional declared passes, explicit unsupported states. Keep provider padding and processing outside the scene contract.

Finalize packages atomically. Keep large outputs outside `.3dm`. Avoid reusable cross-render caches until their invalidation is proven. Stable source identities and a future persisted ID-color registry belong to metadata, not material names or table positions.

## Alternatives

Embedding binary/base64 in JSON expands payloads and allocations. Uploading `.3dm` expands privacy and dependency scope. A mandatory ZIP archive adds extraction validation before there is an export use case. A content-addressed global cache adds difficult document/renderer invalidation.

## Consequences and revisit

Requires storage quotas, cleanup and result provenance. Optional passes can evolve without pretending they exist. Revisit archive export and caching after profiling or a concrete portability requirement. Draft: [Scene Package](../scene-package/specification.md).

# Resolution and framing proposal

Status: implemented and tested in Core for the exact/grid/fixed-canvas cases below. Image resampling itself remains deferred. Core owns the numerical plan; adapters supply model constraints and enforce/verify the native API request.

## Invariants

The Rhino frame is authoritative. Capture never adjusts camera, lens, projection, or view composition to satisfy a provider. Requested final dimensions, capture dimensions, provider canvas dimensions, actual returned dimensions, and exported dimensions are separate fields. DPI metadata does not increase pixels.

Viewport, 2× Viewport, and 4× Viewport refer to **captured image pixels**, not Eto logical display units. Custom initially locks to the current view aspect ratio. If an entered size has a different ratio, explain the conflict and require an aspect-preserving size; do not crop, stretch, or resize Rhino's viewport. Exact integer ratio and provider rounding are different constraints.

## Planning algorithm

1. Validate dimensions and checked pixel multiplication against local capture/memory limits.
2. Intersect the provider's min/max edges, min/max pixels, allowed ratios/sizes, dimension multiples, format, and model-specific restrictions. Unknown constraints cannot be treated as unlimited.
3. Prefer an exact native match. For a supported dimension grid, choose a containing canvas with the smallest padding when possible.
4. Otherwise uniformly fit the entire capture into a supported canvas. Persist the scale, pixel-center transform, and integer content rectangle. Prefer exact-ratio integer content rectangles; never independently round width and height in a way that stretches the source. If no safe rectangle exists, reject the plan.
5. Show any native downscaling, padding, or later resampling before Render. Output beyond the tested provider range is a separately labelled experimental option, never automatic.
6. Validate the returned dimensions. Only remove the precisely recorded padding; never crop source content. Resample the recovered rectangle to the requested output only if this was declared in the plan. Store the unprocessed provider output as well.

Ranking uses least source-information loss first, then least padding, then a deterministic size ordering; optional cost preferences must be explicit. No silent switch to a cheaper model or lower quality. Enforce any model combined-input/output budget as well as output-only limits.

Padding preserves the *input* field of view and gives a deterministic recovery rectangle. It cannot force an image model to keep objects inside that rectangle or avoid reframing. Reject unexpected output dimensions; keep source/generated comparison available even when dimensions match.

## Worked acceptance cases

These are synthetic capability examples, not promises about any named model.

| Rhino capture / requested output | Provider constraints | Plan |
| --- | --- | --- |
| 3000 × 1700 | Multiples of 16, sufficient max area | Canvas 3008 × 1712; source rect `(4,6,3000,1700)`; remove 4/4 left/right and 6/6 top/bottom padding. No source resampling. |
| 3840 × 2160 | Exact dimensions supported | Native and exported 3840 × 2160; no transform. |
| 3840 × 2160 | Fixed 1536 × 1024 | Uniform scale 0.4 → source rect `(0,80,1536,864)`; recover it and resample to 3840 × 2160. Label the final image as enlarged from 1536 × 864 content. |
| 3000 × 1700 | Fixed 1536 × 1024 | Reduced ratio 30:17. Choose content 1530 × 867 (scale 0.51), pad left/right 3/3, top/bottom 78/79. Remove that exact rectangle, then resample. |
| 3840 × 2160 | Requested custom 4096 × 4096 | Reject the conflicting final aspect; changing the requested composition is a Rhino action. |
| Any | Native size, area, and ratio requirements have no solution | Explain unsupported output; no submission. |

First slice is passthrough at exact capture dimensions. Implementing a padding plan in Core does not authorize an adapter to advertise processing until its image operations have been implemented and tested.

Test portrait and panorama frames, odd padding, dimension overflow, 1-pixel boundaries, provider caps, forbidden upscaling, rejected output dimensions, and transformation round trips on a labelled synthetic border image. ID images use nearest-neighbor; reference images need a chosen, tested resampler; depth and normals need dedicated rules before those passes are transformed.

# Scene Package proposal

Status: wire version `1.0` implemented for reference capture. See [structural JSON Schema](scene-1.0.schema.json), Core `SceneValidator`, and synthetic fixtures in `tests/Fixtures`. Auxiliary pass encodings below are future contract intentions, not emitted assets.

## Container and minimum contents

The canonical local representation is a directory containing UTF-8 `scene.json` and lossless PNG assets. The first transport uses multipart file parts, avoiding ZIP extraction and base64 expansion. A future export archive can wrap the same contents without changing their meaning.

```text
scene.json
reference.png          required
edges.png              optional
depth.png              optional, 16-bit grayscale
normals.png            optional
material_id.png        optional
semantic_id.png        optional
object_id.png          optional
section_mask.png       optional
```

Absent files do not imply empty images. Each planned pass has status `available`, `notRequested`, `unsupported`, or `failed`, with a reason where applicable. A required pass that cannot be made fails preflight/capture. An optional omission is recorded and any user-visible loss of requested behavior is reported before submission. The first slice makes only `reference` available.

## Manifest fields

| Field | Meaning / validation |
| --- | --- |
| `schemaVersion` | Major.minor string, initially `1.0`; distinct from protocol, preset, and adapter versions. |
| `sceneId`, `capturedAt` | Random snapshot UUID and UTC timestamp. |
| `source` | Application/build, exporter version, opaque document-session key, units and model tolerance. File path and document title are local provenance by default. |
| `snapshot` | Capture consistency token, explicit document/viewport binding, optional local revision diagnostic; never a promise of global content caching. |
| `camera` | World-space location, direction, up vector, projection kind, full frustum, viewport pixel dimensions, and documented transform conventions. |
| `frame` | Shared capture width/height and pixel convention; all included raster assets must match. |
| `view` | Viewport identifier, optional source Named View reference, modified state when reliably known, display-mode identifier, viewport adornment policy. |
| `passes` | Kind, status, relative filename when available, width/height, MIME, bit depth, channel encoding, SHA-256, byte count, generator version and limitations. |
| `layers`, `materials`, `objects`, `bindings` | Neutral scene metadata with source provenance and completeness; eligible objects are not necessarily visible pixels. |
| `instances` | Block placements resolved to definition identity and composed transform, bounded to 20,000 rows; a document-level placement carries its enclosing chain as `path`. |
| `semantics` | Optional class definitions and assignments, source of mapping, confidence for inferred assignments. |
| `clipping` | Plane equations, enabled state, view participation, finite-depth settings and object/layer inclusion/exclusion rules when known. |
| `requiredFeatures`, `extensions` | Explicit required feature identifiers and optional namespaced additions. |
| `warnings` | Stable codes and user-readable limitations, including partial metadata extraction. |

Reject non-finite numbers, non-positive dimensions, duplicate IDs/asset names, broken table references, unknown required features, unsupported major versions, and inconsistent available-pass metadata. `null` means unknown, not zero or false. Unknown optional fields in a compatible minor version may be retained/ignored; new enum values affecting behavior must not silently become defaults.

Camera coordinates use Rhino world coordinates and declared document units. Matrices serialize as row-major arrays acting on column vectors; specify world-to-camera and camera-to-clip explicitly if included. Store a tested convention identifier. Pixel centers are `(x + 0.5, y + 0.5)` from the top-left, x right and y down. Validate forward/up vectors and frustum/projection consistency. Do not reconstruct two-point views from a lens length alone.

Instance placements serialize the matrix exactly as Rhino defines an instance transform: row-major, acting on row vectors, translation in the fourth row. The three `scale` values are the direction lengths of the first three rows — the basis images of the definition's axes — so anisotropic scale and mirroring stay visible without a decomposition guess. A placement row names its definition and the enclosing chain; it never carries geometry, mesh bytes, or a local file path, and definition names follow the same opt-in disclosure as layer and material names.

The Scene Package always stores the unpadded Rhino capture. Provider input canvas, padding, and output processing live in the separate render plan/result. This prevents provider requirements from becoming scene state.

## Pass semantics

Reference is RGB/RGBA PNG with declared sRGB appearance and explicit alpha interpretation; first-slice opaque output avoids unverified compositing differences. Edges are structural line coverage, with black strokes on white and documented antialiasing. They are not inferred from texture gradients by default.

Proposed depth encoding: camera-forward axial distance in document units, **not** Euclidean ray length or raw GPU Z. `depth.png` is unsigned 16-bit, code 0 is no hit, codes 1..65535 linearly map a declared positive `[nearDepth, farDepth]`; equal-range scenes need an explicit nonzero range. Derive distance from a validated world hit and camera direction. Record normalization and precision; never label Rhino's unexamined grayscale preview as metric depth. Generation is deferred.

Normals: declared coordinate basis and channel mapping from normalized components `[-1, 1]` to RGB `[0, 255]`; alpha 0 means no surface, alpha 255 means valid. A geometry normal is not automatically the visible shading normal. Generation is deferred.

ID images carry integers with `id = R + 256*G + 65536*B`; 0 is background, 1..16777215 are assigned identifiers. No lighting, dithering, gamma conversion, blending, or antialiasing is permitted. Resizing uses nearest-neighbor only. Transparency policy is the nearest visible opaque interpretation specified by that generator, never an undocumented blend of IDs. Unresolved transparency or instance behavior prevents claiming complete coverage.

Color allocation uses a persisted document-scoped registry once ID generation is implemented; IDs are not recycled and hash collisions cannot be silently accepted. Rhino GUIDs remain source identities; material display names and transient table indices are not durable keys. The manifest carries the actual mapping for every package. Cross-project ID equivalence is not promised. Material tables have no singular `layer` field: objects/subobjects bind material identities to layer/object context separately.

`section_mask` is 8-bit grayscale, 255 for visible cut-surface pixels and 0 elsewhere, with a precisely stated coverage/antialiasing policy. It is **not** an inpainting mask and does not describe every cut-away void. Pair it with reference pixels and clipping metadata. Open surfaces without closed caps may have section curves but no fill; do not manufacture a cap to satisfy the mask field.

## Security, storage, and compatibility

Remote manifests use pseudonymous identifiers and only required metadata. Local file paths, credentials, embedded scripts, raw geometry, texture bytes, and arbitrary external URLs are not fields in the transport schema. Treat object/material names as data, not executable instructions. Source-derived text is length-limited and explicitly delimited in prompts.

Use an allowlist of logical filenames. Reject absolute paths, separators, traversal, symlinks, duplicate parts, and unlisted files. Independently validate encoded and decoded limits. Checksums detect corruption; they do not authenticate a server. Validate every included raster's header/dimensions and use a proven decoder before displaying returned images. Server validation must not trust the client.

Write a package in a private temporary directory and finalize atomically after validation. Store large assets in per-user application data, outside `.3dm` and Git. Delete unfinished packages after cancellation/failure and expose retention controls; never delete user-exported results as cache cleanup. Do not implement cross-render scene caching until reliable invalidation is measured.

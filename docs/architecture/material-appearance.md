# Material appearance and model context

Research and proposed next experiment, 2026-10-12. No texture sampler or new upload field is implemented.

The current `BaseColorSrgb` value comes from Rhino's legacy `Material.DiffuseColor`. It is neither a texture average nor the resolved PBR base colour. Rhino documents texture enablement and influence independently of the colour swatch. A texture, transparency, roughness, lighting and mapping can therefore make the visible surface differ from that swatch. [Rhino Materials](https://docs.mcneel.com/rhino/8/help/en-us/commands/materials.htm).

Keep the captured image authoritative for appearance. The composed prompt now explicitly labels material colours as approximate legacy swatches. Existing colour values and opt-in names remain unchanged. This is a clarification of ADR 0012, not a new material-assignment instruction. Eligible material summaries are not proven visible regions, so even an accurate average would not identify where to apply it.

## Is a texture average useful?

Possibly, as a labelled summary of tone. It cannot describe timber grain, concrete pores, tile size, repeating patterns or a mixture of colours. A naïve average can also misrepresent transparent foliage, a disabled texture, an uncoloured data map or an unresolved procedural material. Do not replace every diffuse swatch with the mean of the first image file found.

A future sampler should resolve the effective colour/albedo input, its enablement, tint/influence, colour space and alpha handling. It must distinguish colour maps from roughness, metalness, normal, displacement and AO maps. Define the averaging method explicitly—for example, a linear-light mean converted back to sRGB—and record its provenance and completeness. That method is a proposed descriptive statistic, not a claim about Rhino's complete shading calculation. Reject or label unresolved procedural, nested, subobject and render-content cases; do not invent a resolved appearance.

Use a bounded local cache tied to material/texture changes, with no raw paths or texture files in the provider prompt. Source appearance remains editable in Rhino. Any additional rendered swatch/reference is a separate supported input with visible disclosure, not an automatic extra upload.

## Next experiment

Use the same authored concrete, timber and foliage fixtures to compare image + concise preservation/preset text, the current bounded context, and then a correctly labelled colour summary. Evaluate material region, tone, grain/scale and geometry separately. Keep full scene/camera information locally for capture validation in all variants. Neither OpenAI nor the researched Google/xAI edit guides establishes that a longer Rhino JSON prompt improves fidelity; this requires matched outputs rather than inference from endpoint support.

Build the texture resolver only if this comparison shows useful improvement. Material descriptions may prove more useful than a colour statistic, but opt-in names/descriptions can contain confidential information. The existing privacy choice remains necessary.

## Evidence review and payload decision — 2026-11-27

We have enough evidence to keep the Rhino capture and concise representation instructions as the baseline. We have not established a fidelity gain from automatically attaching layer lists or unlocated material swatches. The authored façade comparison varied the Rhino display style and the preset instructions using one model and seed. It exposed geometry and material drift and found that removing unrelated references to skylights and planting helped that one case. It did not isolate the effect of layer or material metadata. [Façade capture and prompt check](../development/phase-4-review.md#facade-capture-and-prompt-check--2026-11-08)

Provider documentation establishes that reference images and text can be used together for edits. It does not establish that coordinate JSON, a layer list or a palette without assignments improves architectural fidelity. McNeel documents that material appearance combines properties like finish, transparency and texture, and that block materials can be inherited. A physically meaningful input has to explain where it applies in the prepared view. Research on spatial conditioning shows a way to use aligned depth, edge or segmentation maps with a model trained to use those inputs. It is evidence for testing a compatible path, not for sending extra images to every edit endpoint. [Google image-editing documentation](https://ai.google.dev/gemini-api/docs/image-generation) · [Rhino materials](https://docs.mcneel.com/rhino/8/help/en-us/commands/materials.htm) · [ControlNet paper](https://arxiv.org/abs/2302.05543)

| Input | Current path | Decision for the next pass |
| --- | --- | --- |
| Prepared Rhino reference image, preset, preservation instructions and optional prompt | Local managed and cloud edits. The local managed prompt also uses the preset's vegetation policy. | Keep as the common baseline. The visible frame is the authority for camera and materials. |
| Camera, framing, units and clipping | Cloud prompt includes camera position/orientation/frustum, frame plan, units and up to 16 active planes. Actual image transforms and local validation use the captured metadata independently. | Keep local capture and validation detail. Measure whether a concise description of visible effects, such as preserving a cut stair, helps more than raw coordinates. The panel redesign does not change payloads. |
| Material properties | Cloud prompt serializes up to 32 material summaries. The Rhino extractor presently populates legacy diffuse color and opacity for eligible ordinary objects. Roughness and metallic fields exist in the schema. Table entries have no screen-space assignment, texture map or resolved block/subobject appearance. | Do not expand by default. Test explicit, spatially identifiable material cues before choosing new fields. Treat swatches as approximate. |
| Layer or material names and descriptions | Cloud only and subject to the connection setting. Up to 32 layer names and 32 material summaries, with bounded strings. The composer supports descriptions when present. Names by themselves do not map a material to image pixels. | Keep opt-in. Keep raw names out of core prompt instructions. Test short descriptions tied to recognizable visible regions, for example “retain vertical timber grain on the entrance wall.” |
| Geometry, textures and ancillary image passes | The current edit request uses its prepared reference image. The .3dm geometry and texture files are not uploaded as additional inputs. | Keep scene files and raw assets local. Test a separate aligned pass only with an adapter that explicitly supports its intended conditioning semantics, and disclose the extra image. |

### Matched architectural comparison

1. Freeze authored fixtures covering a repeated façade with openings, a clipped stair section, and a material scene with adjacent concrete, timber, glass and vegetation. Use the same prepared image, preset, requested size and exact model within each comparison. Use a matched seed where supported and multiple outputs per case.
2. Compare **A** image plus short preservation and preset prompt, **B** current structured context with names off, and **C** short spatially identifiable architectural or material cues. If B offers a repeatable gain, isolate clipping/camera context, material properties, and opt-in names separately before keeping them by default. Test a compatible aligned pass in a later experiment if geometry drift remains.
3. Blind the review to prompt variant. Check crop and landmarks, opening and stair counts, section voids, planted positions, material boundaries, tone, and texture scale. Score representation quality separately from geometric or material correctness. Record the exact payload, output dimensions, latency, and the tested model/revision.
4. Change each provider's default only when repeat comparisons show a meaningful improvement on the relevant fixtures. Until then the native panel should accurately describe the current inputs and let the user inspect the prepared prompt.

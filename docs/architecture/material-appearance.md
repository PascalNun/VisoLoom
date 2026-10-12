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

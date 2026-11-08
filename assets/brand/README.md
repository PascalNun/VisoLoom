# VisoLoom brand resources

**Name:** VisoLoom

**Tagline:** Generative Representation for Rhino

The primary identity is **Precise / Structured**: a restrained architectural wordmark with the woven symbol on its right. The curved ribbons suggest weaving, materiality, geometry, structure and the transformation of information into architectural representation. Clear over-and-under intersections are essential.

## Files and authority

| File | Purpose |
| --- | --- |
| [source/VisoLoom_Logo.vectorcraft](source/VisoLoom_Logo.vectorcraft) | Original editable VectorCraft source, preserved unchanged. |
| [source/VisoLoom_Logo_geometry_ref.svg](source/VisoLoom_Logo_geometry_ref.svg) | Supplied geometric reference, preserved unchanged. |
| [reference/VisoLoom_Brand_Reference_Board.png](reference/VisoLoom_Brand_Reference_Board.png) | Supplied visual direction for styling, layouts and applications. |

The SVG and editable source define the construction. The board is a visual reference, not an exact vector specification; preserve the source geometry wherever they differ. Do not trace the board, reconstruct approximate paths or redesign the symbol. Sources are separated from references. Add an `exports/` directory only when approved production exports need a home outside the plug-in.

## Approved direction

- Primary layout: horizontal wordmark, symbol right. Secondary layouts: symbol left, symbol above the wordmark, vertical with optional tagline, and standalone symbol.
- Typography: **VISO** light, **LOOM** medium/bold. The supplied SVG refers to Barlow, but this is source information, not a final typeface decision or an installed/bundled font dependency.
- Colour: monochromatic black, charcoal and grays. Subtle grayscale gradients may suggest depth while preserving readable crossings. The board's swatches are reference values, not a finalized palette specification.
- Icons: light-background grayscale, charcoal-background light symbol, and a high-contrast monochrome treatment where necessary. Reduced / Iconic is a possible small-size alternative, not the primary geometry.

The name, tagline, Precise / Structured direction, original geometry and primary layout are settled. Exact typeface, production gradient values, final palette tokens, crossing treatments, line-ending treatment and final small-size/icon exports remain open. The source's current styling is retained without turning every source value into a final brand rule.

These resources guide future Rhino icons, GitHub, documentation, website and application assets. The plug-in UI remains native to Rhino and its platform. Its existing runtime icon stays at `src/VisoLoom.Plugin/Resources/VisoLoom.png`; this organization does not replace it or create a complete icon set, design system or public package.

The panel header uses `PanelSymbol-light.png` and `PanelSymbol-dark.png` in the plug-in's `Resources` folder. These 72-pixel transparent exports use the four original SVG paths in their original order, with the same stroke widths and joins. Only the crop and stroke tones differ for host contrast; neither source file was changed. The adjacent panel name uses the native interface font.

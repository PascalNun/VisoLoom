# Product principles

Status: accepted product principles. Product: **VisoLoom**. Tagline: **Generative Representation for Rhino**.

The architect designs in Rhino; VisoLoom helps represent that design. A successful workflow reduces representation setup while keeping architectural decisions with the architect.

**Geometry preservation is a core product objective across every representation mode.** Aim to keep the architecture, terrain, hardscape, openings, stairs, proportions, camera composition and section geometry unchanged. This objective guides provider selection, scene passes, prompt composition and result evaluation. A generative model cannot guarantee preservation on every output; architectural deviations remain defects to detect and reduce. An attractive image with altered architecture falls short of the intended result.

Representation can refine linework, texture, material appearance, lighting and atmosphere within the architect's chosen intent. Deliberately established material character and lighting direction should guide the result; a material or lighting study may explicitly vary them. Where the selected mode permits vegetation interpretation, retain its approximate location, scale and overall mass. That allowance does not extend to redesigning the architecture.

**Make the intended appearance visible in Rhino first, to the level appropriate for the representation.** Geometry, asset form, surface color, texture scale and deliberate lighting should already communicate the design. Image editing adds representational refinement. Descriptions supplement that visible reference. An arbitrary blue surface labelled “concrete” is not the preferred input for a faithful concrete visualization; false colors belong to separately validated identification passes. This does not require a finished photorealistic render or complete PBR setup before an early concept study.

1. Rhino owns the view, Named Views, geometry, clipping, layers, visibility, materials and selections. VisoLoom reads them and captures a moment in time. Selecting a saved view is read-only; an explicit **Show named view in Rhino** action may restore it visibly before capture. Capture, generation and returned results never restore views automatically.
2. Start with sensible defaults: active display mode, current view, a configured destination, one result, viewport resolution. An ordinary model does not need semantic tags or finished PBR materials.
3. Every primary control must change an architectural outcome or explain a consequential choice. Technical diagnostics are available without occupying the main workflow.
4. Pursue unchanged geometry throughout the generation workflow and evaluate results against the Rhino source. Communicate model limitations honestly while keeping this objective central; exact source framing and generated architectural fidelity require separate checks.
5. Resolution is pixels first. Explain native generation, padding, enlargement, and actual output where they differ.
6. Remote destination and transmitted information are visible. A local-only policy must be enforced, not just displayed.
7. A job retains its captured scene while the architect continues modelling. Results never restore a camera or change the model automatically.
8. Errors explain what happened and the next valid action. Do not silently discard sections/material context, switch providers, crop, reduce quality or retry a billable submission.
9. Use precise, calm architectural language, native controls, restrained color, compact spacing, and no decorative animation. Status must not depend on color alone.
10. Preserve source comparison and provenance. “Looks convincing” is insufficient if an opening, stair, section void or proportion was altered.
11. Keep one ordinary Render workflow across local and cloud models. A local capture preview checks the input; it is not a generated draft. Completed images are saved automatically outside temporary scene history, with an optional additional output folder and an explicit save-failure message.

The loom metaphor informs product identity; it does not become a naming system for jobs, passes or buttons.

Reference-image selection, custom display-mode validation and lighting/material benchmarks are defined in the [architecture](../architecture/proposed-architecture.md#reference-image-and-visible-intent). The planned [material collection](../roadmap.md#rhino-material-collection) supplies ordinary editable Rhino materials; it never limits users to a VisoLoom catalogue.

## Language for user-facing surfaces

Decided 2026-11-21: surfaces a designer reads — the README's front page, user journeys, marketing or release notes, and future guides — are written in the designer-facing voice used in [RhinoSpatial](https://github.com/PascalNun/RhinoSpatial): name the audience and their profession; lead with the outcome and the task, not the mechanism; organise tables by *what the user needs*, then the surface that provides it; prefer "Captured, rendered, compared, saved" to internal vocabulary; and state status honestly in adoption terms — what works today, what is wired but unproven, and what remains developer setup. Technical substance is never dropped; it is placed where a reader can act on it.

Development documents — ADRs, plans, the implementation review, architecture and this page — keep the repository's precise internal register: evidence language, dated sections, exact bytes, hashes and statuses. Neither register argues with the other; they serve different readers.

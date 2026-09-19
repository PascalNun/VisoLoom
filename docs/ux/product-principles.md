# Product principles

Status: proposed UX before implementation. Product: **VisoLoom**. Tagline: **Generative Representation for Rhino**.

The architect designs in Rhino; VisoLoom helps represent that design. A successful workflow reduces representation setup while keeping architectural decisions with the architect.

**Geometry preservation is a core product objective across every representation mode.** Aim to keep the architecture, terrain, hardscape, openings, stairs, proportions, camera composition and section geometry unchanged. This objective guides provider selection, scene passes, prompt composition and result evaluation. A generative model cannot guarantee preservation on every output; architectural deviations remain defects to detect and reduce. An attractive image with altered architecture falls short of the intended result.

Representation can change linework, texture, material appearance, lighting and atmosphere. Where the selected mode permits vegetation interpretation, retain its approximate location, scale and overall mass. That allowance does not extend to redesigning the architecture.

1. Rhino owns the view, Named Views, geometry, clipping, layers, visibility, materials and selections. VisoLoom reads them and captures a moment in time.
2. Start with sensible defaults: active display mode, current view, a configured destination, one result, viewport resolution. An ordinary model does not need semantic tags or finished PBR materials.
3. Every primary control must change an architectural outcome or explain a consequential choice. Technical diagnostics are available without occupying the main workflow.
4. Pursue unchanged geometry throughout the generation workflow and evaluate results against the Rhino source. Communicate model limitations honestly while keeping this objective central; exact source framing and generated architectural fidelity require separate checks.
5. Resolution is pixels first. Explain native generation, padding, enlargement, and actual output where they differ.
6. Remote destination and transmitted information are visible. A local-only policy must be enforced, not just displayed.
7. A job retains its captured scene while the architect continues modelling. Results never restore a camera or change the model automatically.
8. Errors explain what happened and the next valid action. Do not silently discard sections/material context, switch providers, crop, reduce quality or retry a billable submission.
9. Use precise, calm architectural language, native controls, restrained color, compact spacing, and no decorative animation. Status must not depend on color alone.
10. Preserve source comparison and provenance. “Looks convincing” is insufficient if an opening, stair, section void or proportion was altered.

The loom metaphor informs product identity; it does not become a naming system for jobs, passes or buttons.

# User journeys

Status: proposed. Journeys describe the intended product; Phase 3 first proves capture and transport with a labelled development backend.

## First useful representation

Prerequisite: one allowed real engine is configured. The user sets the view in Rhino, opens VisoLoom, chooses Mode, optionally adds a prompt, chooses Resolution, and presses Render. If mode/resolution defaults already fit, Render is the only required panel action. No additional camera, material import, scene wizard, or pass selection is required.

Infer camera/projection/frame, units, current display mode, eligible visible materials/layers, active clipping and applicable preset context. Show view and destination readouts so the inference is reviewable. If the active view differs from a restored Named View, identify it as current/modified when reliably known; never pretend the saved view was used.

After capture, the user can continue modelling. The result opens inside the panel with Source/Result comparison and Save. View and timestamps refer to the capture, not the current document state.

## First-time setup

If no engine is configured, the panel shows the current view and a single Set up engine action. Settings offers an allowed local/office endpoint or an implemented cloud provider, never a catalogue of apparently working placeholders. The user sees endpoint/provider, authentication requirement, destination and payload summary before saving the connection. Credentials persist only in supported OS storage; otherwise offer session-only use.

The first development milestone has a `VisoLoom Reference` engine, described as **Returns the Rhino capture unchanged**. Style Mode and Prompt are disabled with that explanation; the action is **Test capture**. This developer exception avoids presenting passthrough as a generated representation. The normal product action remains Render once a generative engine exists.

## Section perspective

The architect sets Rhino's clipping planes and activates the section view. VisoLoom detects active clipping and uses the captured reference plus available clipping context. The panel indicates when a section is present. Section treatment appears only if implemented and meaningful for the selected engine.

If an explicitly requested mask-dependent treatment cannot be supported, Render is blocked with the reason and available alternatives. A standard reference-based section request may proceed with a clear statement that no section mask is used and source comparison is required. Do not fill removed geometry or reinterpret a black cut fill as a request for a new façade.

## Material study

The architect keeps Rhino's assigned material and may supply a representational description such as “light washed brick.” The future material-description inspector attaches to Rhino identities; it does not recreate a material editor. A region-specific change requires a tested regional adapter capability. If only whole-image prompt context is available, say so and do not label the change as object-local.

## Long render, cancellation, and recovery

Show the captured view thumbnail, job stage, elapsed time, and genuine progress where supplied. A queued job says queued; an unknown percentage is indeterminate. Cancel before submission stops local work; after submission its label/explanation reflects remote capability. If only waiting can stop, use Stop waiting and explain that generation may continue.

A network failure preserves the job ID and offers Reconnect when possible. Retry never silently changes preset, model, view, or quality. If the failed job can safely be retried from its capture, offer Retry this capture; rendering the newly edited model is a distinct Render current view action.

## Future batch

Choose existing Rhino Named Views and representation/output settings once. Each job must explicitly define when geometry/visibility state is captured; a Named View is not a full scene-state snapshot. Before implementing batch, decide whether all views share one captured document revision or capture current state sequentially. Do not introduce a VisoLoom view collection to hide that unresolved behavior.

## Usability acceptance

Test with an untagged model and a returning user. Observe time to first submission, unnecessary setting changes, confusion over current/captured view, understanding of output size, cancellation expectations, and ability to identify an altered opening. These are proposed tests; no usability sessions have been run.

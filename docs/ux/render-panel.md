# Render panel

Accepted design direction, 2026-11-27. This is the implementation target for the Rhino dock. The native implementation covers the setup layout, model and preset choices, result inspection, and paged project history. Docked operation on supported hosts remains part of acceptance.

![Render panel with the grouped model picker open](render-panel-assets/model-picker.png)

[Interactive mockup](render-panel-mockup.html) · [Settings design](settings-mockup.html). GitHub shows the HTML source. Download or clone the repository and open the mockup in a browser to try the controls. It uses authored façade-study images and simulates model choice, settings, preset editing, and rendering without a connection.

## Layout and controls

Use native Rhino/Eto controls and theme colors. Keep the vertical order at narrow and wide widths, with labels stacked where needed. Start with a small logo, **Render**, and a visible **Settings…** action in the header. It opens the existing Settings control and remains available when setup is hidden. When Library is ready, put workspace navigation in the header. An image-focused **Hide setup** / **Show setup** action belongs with the image controls.

| In order | Interaction |
| --- | --- |
| **View** | Choose the current view or a Rhino Named View. Retain the existing Named View browser and explicit **Show named view in Rhino** action. Rhino owns views and cameras. |
| **Representation** | Choose a built-in or personal preset. **Edit preset…** belongs beside the selector, with stacking at narrow widths. |
| **Model** | Choose one concrete model. The closed control shows its name and where it runs. The selected model becomes the project default when the Rhino file is saved. |
| **Sampling steps** | Show this near Model for a local profile with a documented override. Start with that model's default, then show valid steps. |
| **Prompt** | Optional additions specific to this image. Keep the text when switching model or representation. |
| **Output size** | Keep **Viewport**, **2×**, **4×**, and **Custom** visible. In Custom support direct entry of both dimensions, aspect lock, and the existing safe-frame behavior. Show the requested output size and disclose provider scaling or padding before submission. |
| **Scene guidance** | Collapsed by default and in a stable place. Summarize what the destination uses. Under it, retain **View full prompt…** for the prepared request. |
| **Preview / Render** | One primary action reflecting readiness. Preserve the reviewed capture flow below. |
| **Image and history** | One shared image surface, then Recent renders. Show Source, Result, Compare, and access to opening and exporting a full-resolution file. A separate Prepared input action returns to the exact input for the next render when viewing a previous result. |

### Choose the exact model

Open the choices inside the dock, directly beneath **Model**. Show **This computer** first, then a divider and **Cloud**, with provider names above their model rows. Left-align the names and mark the exact selected model with a check. Scroll long lists within the picker. Click a model or activate its row with Enter to choose it. Escape collapses the list and returns focus to the Model field.

Display the selected model and destination in the dock. Use readable model names, with exact IDs and versions available in the chooser. For a family with multiple variants, use a distinct model or snapshot label, such as **Nano Banana 2 · Gemini 3.1 Flash Image**. Populate the chooser from installed local capabilities and each provider's currently available, supported image-edit profiles.

Settings handles connection, credential, model discovery, and installation. Opening the picker or selecting a model makes no generation request. Retain the view, representation, prompt, and requested output size on a switch. Revalidate size and expire incompatible previews. Restore the last selected model as the normal Model selection. Keep an unavailable saved choice visible and explain how to reconnect it in Settings or select another model. A choice stores only the provider, model ID, and version. Preview and Render remain explicit actions. Defer other destinations until the production transport supports them. Keep the internal capture test available through diagnostics.

### Representations

Keep built-ins as stable starting points, specify their look in a one-sentence description, and write concrete style instructions about linework, lighting, tonal range, or texture. Keep project-independent geometry and framing instructions in the shared composer. Keep built-in names and contents intact. Personal presets live with user settings and can be named, updated, duplicated, or deleted with confirmation.

In the editor, **Save as new preset…** creates a personal copy of the displayed draft. **New preset** starts with blank fields. **Save preset** updates a personal preset. **Reset changes** reloads the fields as they were when the editor or draft opened. It never writes to storage. Show the compact list beneath **Representation**, with personal choices grouped under **My presets**. Avoid a UI quota and keep bounded storage and clear save errors. Preserve preset identity and revision in render provenance. The editor also keeps the existing copy and confirmed-delete paths. [Preset storage and editing decision](../adr/0029-personal-preset-editor.md)

### Preview and progress

The native workflow first prepares the exact model input, then renders that reviewed image with its prepared prompt. When local models are selected and the installed companion is stopped, the primary action is **Start engine**. Startup checks model files without capturing or submitting the view. Use **Preview view** when there is no capture, **Update preview** when inputs change, and **Render view** when the reviewed preview is ready. Selecting an older result does not replace the next render's source. Preserve the last usable image during preparation or provider failure. Show measured step progress when the local runner reports it. For cloud wait states, show the provider with elapsed time or indeterminate progress. Label a local cancellation **Cancel** and a provider wait that cannot cancel generation **Stop waiting**. [Reviewed preview contract](../adr/0015-preview-before-submission.md)

## Recent renders and project history

Keep up to three newest thumbnails under the image. This is quick access. Add **All project renders…** next to the strip. It opens a bounded-height, vertically scrolling list or native dialog for the active project, newest first. Selection shows that render in the existing image surface. Keep Open and Save PNG available where the image exists. Enable Compare when the original source is available. Show a readable missing-file row when a saved output has been moved or is on another machine. Load thumbnails as needed and full images on selection. Prefer project history to an ever-growing dock.

The panel retains up to three source/result pairs in memory, subject to its memory budget. The project list includes every output reference recorded in the Rhino file, plus any additional session result. It pages through long lists and reads pixels only for the visible recent thumbnails and the selected image. Saving the Rhino file persists result references. Existing V1 entries are read alongside the per-result records. Older output files that had already fallen out of the V1 index may still exist on disk. [Project render index](../adr/0034-project-render-index.md)

## Acceptance

Validate the layout docked at narrow and wide widths in Rhino on macOS and Windows, in light and dark themes and with keyboard navigation. Confirm that changing models updates the project default and invalidates the prepared input without uploading. Exercise new and copied presets, draft reset, paired Source/Result, inline Compare, full-resolution inspection, and Save PNG. Save and reopen a project with more than one page of records. Verify the existing legacy rows and the missing-file state.

Review [scene-guidance evidence](../architecture/material-appearance.md) before changing what is sent. The panel cleanup can ship independently of the context experiments.

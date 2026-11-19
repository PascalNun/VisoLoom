# Low-fidelity panel wireframes

Status: accepted interaction target. Native capture-test and local preview actions are implemented; visual and usability validation in a supported host remains open. Use native Eto widgets and Rhino theme metrics. Width is illustrative; test roughly 300–420 logical pixels, high-DPI display scaling, dark/light themes and keyboard navigation.

## Ready with a real engine configured

```text
┌──────────────────────────────────────┐
│ VisoLoom                    Settings │
│                                      │
│ View                                 │
│ Tiefhof Schnitt 02 · Perspective      │
│ Active clipping planes               │
│                                      │
│ Preset     [Competition Sketch    ▾] │
│                                      │
│ Prompt (optional)                    │
│ ┌──────────────────────────────────┐ │
│ │ Warm grey wash, quiet atmosphere │ │
│ └──────────────────────────────────┘ │
│                                      │
│ Resolution [2× Viewport            ▾] │
│            3840 × 2160 px             │
│                                      │
│ Model      [Configured model      ▾] │
│ Office server · approved destination │
│                                      │
│ ▸ Advanced                           │
│                         [ Render ]   │
└──────────────────────────────────────┘
```

The model selector and destination row are illustrative, not a claim that model switching or an office engine exists. View label follows Rhino; if Named View association is uncertain, use Current view plus viewport name. No VisoLoom camera chooser or material library.

## Prompt preset selection

```text
Preset  [Competition Sketch ▾]
  Photorealistic
  Competition Sketch
  Architectural Sketch
  …
  My saved presets

[Edit preset…] → readable instructions → [Save as new preset…]
Prompt (optional): additions for this render
```

This is a product target. Supplied presets are reusable prompt starting points; personal copies can be named and edited without losing the originals. Changing Preset keeps the optional prompt, selected model and requested resolution. Geometry preservation remains the shared objective. See the [preset contract](information-architecture.md#prompt-presets). The implemented panel now has a Preset dropdown and optional prompt for OpenAI. The local capture test disables them. A personal preset editor remains future work.

## Model selector and Settings

```text
Model  [Choose configured model ▾]
  This computer
    FLUX · configured local model
  Cloud
    OpenAI · configured editing model
  Manage connections…

Settings → Connections
  Local studio    This computer    Ready
  OpenAI          Cloud            Credentials needed
  [Add connection…]

Connection details
  Name            [Local studio             ]
  Provider/server [Supported local server  ▾]
  Address         [http://127.0.0.1:…        ]
  Credentials     [session / supported OS store]
  [Check connection]
  Models          [Available installed model ▾]
  Default model   [Use for new renders]
  [Save]
```

Provider names illustrate future adapters; the current reference build must not present them as selectable engines. The address field is relevant to server connections; a direct cloud adapter uses its vetted destination instead of an arbitrary URL. Settings is a native view/dialog reached from the panel. Keep endpoint and credential details out of the ordinary render flow after setup. Connection checking generates no image.

The primary selector uses a native list, keyboard navigation and clear text status. It lists configured choices rather than a marketplace. Unavailable choices explain why; switching to an available choice preserves intent and shows any capability conflicts inline. A destination change remains visible. A running job and the last result continue to display the model they actually used. See the [settings contract](information-architecture.md#model-choice-and-connection-settings).

## Resolution requires processing

```text
Resolution  [Custom ▾]   3840 × 2160 px
Native image: 1536 × 1024 px
Image area:   1536 × 864 px
Padding removed; enlarged to requested size.
[Details]
```

Numbers illustrate a fixed-size synthetic provider. The difference stays visible before submission; it is not hidden in Advanced. The source composition is retained by the plan, but a generated image can still alter it. No “native 4K” label for an enlarged image.

## No configured engine / development engine

```text
View: Perspective · current Rhino view
No engine configured.
[Set up engine]

After developer setup:
Engine: VisoLoom Reference · This computer
Returns the Rhino capture unchanged.
Mode and Prompt unavailable for this engine.
Resolution [Viewport ▾]   1920 × 1080 px
[Test capture]
```

This is the first-slice development state. Real provider names can appear in settings as Planned / Not implemented, but cannot be selected for execution.

## Running and result

```text
Captured: Tiefhof Schnitt 02 · 14:32
[source thumbnail]
Rendering · 00:48     [indeterminate progress]
[Cancel]             (only if it can be requested)

Completed · Tiefhof Schnitt 02 · captured 14:32
[ Source | Result ]  [Fit | 100%]
┌────────────────────────────────────┐
│              image                 │
└────────────────────────────────────┘
3840 × 2160 px · configured engine/model
[Compare] [Save PNG…] [Details]
```

Source/Result toggling and the movable comparison divider share the same zoom and framing. Fit and 100% control the image viewer. The image region can expand without taking over Rhino's workspace.

## Errors and edge states

| State | Message / action |
| --- | --- |
| No supported model viewport | “Activate a model viewport in Rhino.” Disable submission; layout/detail support is unverified. |
| View/document changed during capture | “The scene changed during capture. Capture the current view again.” |
| Requested section mask unavailable | “This engine cannot use the requested section treatment.” Offer the relevant setting/engine change. |
| Server offline | “Cannot reach the configured server.” Show endpoint and Retry connection. |
| Missing credentials | “Sign-in details are required for this engine.” Open settings. |
| Native size impossible | “This engine cannot produce the requested frame with the allowed processing.” Show viable choices. |
| Connection lost after submit | “Connection lost; the render may still be running.” Reconnect; no automatic new render. |
| Remote cancellation unsupported | “Stop waiting? Generation may continue.” Explain capability inline; no fake cancelled success. |
| Output failed validation | “The engine returned an unexpected image size.” Keep source and diagnostic metadata; no silent crop. |
| User navigates after capture | “Result from captured view at 14:32.” Do not restore that view automatically. |

Labels, focus order, Enter/Escape behavior and control sizing should follow host conventions. Escape must not accidentally cancel an unrelated Rhino command; command-scoped and panel-scoped actions need explicit ownership. Progress messages must not repeatedly steal keyboard focus.

## Implemented preview flow — 2026-10-12

Choose view/style, preset/prompt, resolution and model → **Preview view** → inspect the prepared model input with **Enlarge preview…** and **View full prompt…** → **Render** or **Test capture**. The preview works without credentials or a running service. Render submits the same image and text, with no second capture/transform. Camera, scene, size, intent, model or connection/privacy changes require **Update preview**. A submitted preview cannot be sent again. An expired image remains labelled out of date; Render stays disabled.

The preview is the actual model canvas, including recorded padding. It is not a live Named View thumbnail or a promise of independent framing. The resizable native inspection window fits the image; 100% zoom and comparison tools are still targets. No safe-frame overlay is implemented yet. Keep the chosen Rhino display style rather than selecting an untested “best” style. See [ADR 0015](../adr/0015-preview-before-submission.md).

## Panel polish — 2026-10-13

The implemented panel now groups View → Image → Output size → Preview and render → Result. Settings is in the header; manual local connection setup is inside Settings. Image keeps the model selector and a short cloud/local destination visible. OpenAI remains selectable with a needs-setup label before credentials are configured, allowing local preview while Render stays disabled. Preset and the collapsible optional prompt appear for image editing; capture-test mode gives a short explanation instead of disabled intent controls. Custom dimensions appear only for Custom. Empty previews/results and idle cancellation controls are hidden. Wrapped labels measure their height as text or panel width changes.

**Browse named views…** opens a native list and a selected composition thumbnail. Choosing a name changes only VisoLoom's source selection; **Show named view in Rhino** remains explicit. Rhino owns saved cameras, their names and editing. Thumbnails are local navigation aids; the reviewed model input still comes from Preview view after showing the view. Broader thumbnail lighting/clipping fidelity and native Windows interaction remain acceptance cases. See [ADR 0016](../adr/0016-panel-and-named-view-browser.md).

## Compact Rhino panel — 2026-11-08

View → Representation → Model → Prompt → Output size is the main form. It uses aligned native selectors at wider widths and stacks labels and fields when narrow. The header and Settings stay visible while the form scrolls. The approved woven symbol is exported directly from the four source paths, with light and dark contrast variants. **Named views…** browses Rhino's named views and **Show in Rhino** explicitly restores the selected view. Representation and prompt appear for image editing.

Output size offers **Viewport**, **2×**, **4×** and **Custom** in one row. The linked width and height fields appear for Custom. The readout distinguishes saved size from the model canvas. **Scene guidance** is collapsed by default and flags a section when the capture records an active clipping plane for that view. Expand it for the captured scene context and exact prepared prompt. The destination disclosure stays visible. Connection details, name sharing and image saving stay in Settings.

The main button advances from **Preview view** to **Render view** or **Test capture**. It only sends a valid, reviewed input; changes or a previous submission require **Update preview**. Browsing a result switches the action to **Review source** before sending can be offered again. Pending work shows status and the existing cancellation or stop-waiting action.

One preview area displays the prepared input or a recent image. Select from up to three recent thumbnails in the current document session. A list takes over when the panel is too narrow for thumbnails or an image cannot be thumbnailed. The preview and Compare identify the Rhino view and display style recorded at capture. Capture tests are labelled in the preview and navigation. **Open…**, **Compare…** and **Save PNG…** use the selected image. **Compare…** opens with a source/result toggle and offers a movable **Wipe** divider. Saved PNGs remain on disk after the session. Browsing past sessions is future work.

The source-linked panel passed a light-appearance Rhino 8 check at 260 and 430 points with no horizontal overflow. Selecting an older thumbnail updated the result. Changing to Custom expired the prepared input. The comparison viewer passed its zoom, toggle and divider checks. The blank test document and its views were unchanged. The registered dock, dark appearance and Windows-native interaction still need host checks.

## Settings and shared wording — 2026-11-12

Settings uses three native pages: **Models**, **Connections** and **Image saving**. Models explains each existing choice in plain language and routes setup to Connections. Compatibility/storage, existing model folders and detailed setup reports are collapsed. Connections keeps local and cloud setup accessible, with technical local-service instructions and data/billing details under disclosures. Image saving exposes the automatic folder and the optional additional copy without a nested dialog. There is no Library navigation until that feature exists.

User-facing location labels use **This computer** and **Cloud**. Compatibility details still describe real hardware and operating-system limits: the current local engine supports Apple Silicon, while Windows local inference remains planned. Disabled local setup on unsupported systems does not prevent cloud configuration or source preview.

The Render panel uses **Source image**, **Preview view**, **Update preview**, **Review source** and **View full prompt…** consistently. One status area retains progress, completion and error messages; ordinary guidance adapts to the reviewed source and connection state. The output-size readout shows export and model dimensions; its tooltip carries padding and experimental-resolution details. Capture, privacy, submission and result-comparison behavior are unchanged.

The source-linked panel passed Rhino 8 dark-appearance checks at 260 and 430 points, including result navigation, source review, expiry after changing size, preserved error feedback and no horizontal overflow. Settings also passed dark-appearance checks at 360 and 480 points, including all-page navigation and API-key focus without changing preferences. This supplements the earlier light-appearance check. The revised Settings in light appearance, registered dock and Windows-native interactions still need host acceptance.

## Usability and informed control — 2026-11-19

The interface assumes a capable designer. Use plain, precise language and make the information needed for a decision visible. A compact layout must not conceal model limitations, data sharing, hardware fit or storage requirements. Developer instructions and raw paths belong in clearly labelled setup details, with the original functions still accessible.

The combined panel retains the selected model through preview, rendering and project-model restoration. Optional project persistence sits under **Project model**, with **Save model with project** and **Remove saved model** actions. The main form shows the saved output size; model padding is explained in its tooltip. When generation requires resampling, the generated content dimensions and export resizing remain visible. Ordinary readiness instructions are omitted; progress, errors and required connection actions remain visible.

Settings has **Models**, **Connections** and **Image saving** pages. Models shows purpose, hardware, storage and setup requirements directly for the selected option. Connections shows the last checked local model and reports the actual model returned by the service. Cloud data-sharing and billing summaries remain visible, with the full details available nearby. Connection failures preserve actionable explanations. Existing model files, request inspection and manual connection controls remain accessible.

The combined source-linked panel passed 14 native checks at 260/430 points, and the isolated project-model check passed six persistence/availability checks. Settings passed all six navigation/focus/overflow checks at 360/480 points, with the original document and preferences unchanged. The final copy pass shortened the model requirements without removing hardware, storage or licensing limitations. Registered-dock behavior, light appearance for this revision and Windows-native interaction remain separate acceptance cases.

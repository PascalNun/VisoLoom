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
│ Representation [Competition Sketch ▾]│
│ Model      [Configured model      ▾] │
│ Office server · approved destination │
│                                      │
│ Prompt (optional)                    │
│ ┌──────────────────────────────────┐ │
│ │ Warm grey wash, quiet atmosphere │ │
│ └──────────────────────────────────┘ │
│                                      │
│ Output size [2× Viewport           ▾] │
│            3840 × 2160 px             │
│                                      │
│ ▸ Scene guidance                     │
│           [ Preview view ] [ Render ]│
└──────────────────────────────────────┘
```

The view label follows the active Rhino view or the selected Named View. Use the current view plus its viewport name when a Named View association is uncertain. The model row follows the selected destination. Render uses the prepared preview.

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
    FLUX.2 klein 4B
  Your server
    Studio workstation · configured model
  Cloud
    OpenAI · configured model
    Black Forest Labs · FLUX 3 Image Klein

VisoLoom Settings

Local models
  FLUX.2 klein 4B · Installed
  [Browse models…] [Use existing files…]
  Expanded catalogue
    FLUX.2 klein 4B · Compact · 5.2 GB        [Installed]
    FLUX 3 Image Klein · Compact           [Get weights…]
    Qwen-Image-Edit-2511 · Workstation         [Download]
    ▸ More local models
    Download folder · ~/Models/VisoLoom         [Change…]

Cloud providers
  OpenAI · Connected · Key saved              [Manage…]
    Image models
      GPT Image · Sunburst 2.5
      GPT Image · Flare
    [Refresh models]
    ▸ Change API key
    [Disconnect & forget key]
  Google · No API key                         [Connect…]
  xAI · No API key                            [Connect…]
  Black Forest Labs · No API key               [Connect…]
    BFL API key [••••••••••••••••••]
    Keys are kept in this computer's credential store.
    [Connect]

Your server
  Studio workstation · Connected               [Manage…]

Image saving
  Saved automatically                           [Options…]
  Additional copy · project renders             [Change…]

                                                  [Done]
```

This is the product target. The current build registers an OpenAI connection and a prepared local development service. Each additional provider becomes actionable when its integration is available. The same Eto control serves Rhino Options/Preferences and the panel shortcut. Closing Settings leaves the user in the same Rhino workspace.

Open provider forms inline under their own rows. On connection, keep the row open and show the provider's compatible image models. Show **Refresh models** when the provider supports account discovery and put the result beside that action. Keep the masked replacement field under **Change API key** for connected accounts. Black Forest Labs shows its curated hosted image-edit options after a key check. **Get weights…** opens publisher and licence information plus the path to link existing files. **Add server…** opens the name, HTTPS address and token fields. **Options…** opens the additional-copy folder chooser. At narrow widths, wrap labels and move secondary actions below a row as needed. Keep **Browse models…**, **Use existing files…** and provider setup usable with the keyboard.

Select a model in Render after setup. Retain the representation, prompt and requested export size when switching. Recompute the prepared frame plan and show the destination. For a temporarily unavailable pinned choice, show its recovery action. The running job and last result show the model they actually used. [Settings behaviour](information-architecture.md#model-choice-and-connection-settings)

### Native Settings layout

| Mockup section | Native controls |
| --- | --- |
| **Local models** | One row per installed model: the running engine reads Connected, a download reads Installed and a linked folder reads Existing files linked. The Local engine line shows Running or Off and a Check engine button. **Browse models…** opens the reviewed catalogue, **More local models** and the download folder. **Use existing files…** opens the folder picker and the checks for linked files. |
| **Cloud providers** | OpenAI, Google, xAI and Black Forest Labs each show their connection and key state. **Connect…** and **Manage…** expand the provider in place. Connected providers show their model list above the masked key controls. OpenAI, Google and xAI offer **Refresh models**. Black Forest Labs offers **Check connection** and shows its curated list. A failed key replacement leaves the active connection and model list in place. |
| **Your server** | Keeps a saved server address and optional access token, with a read-only Check connection against the server's capabilities. Rendering through a server arrives with the office transport ([ADR 0017](../adr/0017-local-inference-and-model-prompts.md)). |
| **Image saving** | Shows automatic saving with **Options…** for choosing or removing an additional folder and opening saved images. |

Both Rhino Options and the Render panel use the same scrolling page. The standalone Settings dialog puts **Done** beneath it. Rows use Rhino's native controls, with separators between sections and insets for expanded controls. Model selection stays in Render.

Section dividers get more space and contrast than the lines between providers. Expanded controls have a left rule and indentation. Model names and provider names sit above quieter status text. Green confirms a connected provider, whose status reads **Connected · Key saved**; no consent or remember tick exists on the row ([ADR 0031](../adr/0031-consent-on-connect.md)). The provider row states what a render transmits. The local list gives the running model its own name and connection status.

### Project model in Render

```text
Model [GPT Image · Sunburst 2.5 ▾]
OpenAI · Cloud
Project model · No model saved
[Save model with project]

After saving a model and switching to Flare in this session:
Model [GPT Image · Flare ▾]
OpenAI · Cloud
Project model · GPT Image · Sunburst 2.5
Using a different model for this session.
[Replace saved model] [Remove saved model]

After reopening without access to the saved model:
Model [GPT Image · Sunburst 2.5 · Unavailable ▾]
Project model · GPT Image · Sunburst 2.5 · Unavailable
Restore access in Settings or choose another model.
```

Save the Rhino file to keep a changed project model. The names and connections in the [clickable mockup](settings-mockup.html) are examples. Use the [implementation plan](../development/model-selection-plan.md#save-a-model-with-a-rhino-project) for persistence, recovery and acceptance cases.

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

This is the first-slice development state. Keep **Test capture** available for development. The Render selector exposes supported installed or connected choices and their relevant setup states.

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

The preview is the actual model canvas, including recorded padding. It is not a live Named View thumbnail or a promise of independent framing. The resizable native inspection window fits the image; 100% zoom and comparison tools are still targets. The safe-frame overlay ([ADR 0030](../adr/0030-safe-frame-framing.md)) shows the centered Lock-off frame in the selected view with the excluded edges dimmed, suspended for every capture; its native visual walkthrough is pending. Keep the chosen Rhino display style rather than selecting an untested “best” style. See [ADR 0015](../adr/0015-preview-before-submission.md).

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

## Settings connections view — 2026-11-21

The Connections page is now the saved-connections list the [settings contract](information-architecture.md#model-choice-and-connection-settings) describes ([ADR 0028](../adr/0028-settings-connections-view.md)): one row per destination — this computer, OpenAI, Google, xAI, BFL — always visible with its state and, when it cannot render, the reason (`missing API key`, `disabled`, `not set up`, `rendering off`). Selecting a row shows that connection's friendly name, destination, status, supported models, credentials, its toggles and the explicit **Check and save connection** / **Disconnect and forget key** actions; **Add connection…** opens one chooser for all destinations. Each cloud provider is described once as data, so its disclosure rows, tested/curated labels and honest BFL language stay attached to that provider and cannot drift between forms.

The Models tab's router bug is fixed here: every **Set up *…** button used to focus OpenAI's key field; the handoff now selects the provider it names. The local row keeps the enable/check/setup block, with the last-checked model now refreshed live from the coordinator instead of being a construction-time snapshot.

Native checks to re-run: the settings walkthrough must exercise the new list (all pages, 360 pt narrow without horizontal overflow, OpenAI key focus after **Set up OpenAI…**, preferences unchanged), with a fresh eye on focus delivery into a just-selected detail panel. Windows paths and the registered dock remain separate acceptance cases, as before.

## Personal preset editor and representation default — 2026-11-21

The Representation row gains **Edit preset…** beside the Preset dropdown ([ADR 0029](../adr/0029-personal-preset-editor.md)), implementing the accepted preset paragraph. The editor exposes the selected preset's name, short description and representation instructions, with the preservation-intent and vegetation-policy fields editable in a collapsed detail and a standing note that the geometry-preservation objective is always composed into the prompt. A supplied preset can only become a named personal version (**Save as new preset…**), keeping the starter in the list; a personal preset can be saved in place, copied, or deleted behind an explicit one-time confirmation that names the consequence. Preset names must stay unique in the dropdown, and a personal id is always `personal-` prefixed so it can never collide with a supplied id that a document pin or intent.json already recorded.

The last chosen preset now persists as this user's default and is restored when its id still exists; a deleted id falls back to the first supplied preset. The dropdown shows the selected preset's description as its tooltip. Personal presets live in one JSON document in application data — outside `.3dm` and Git — validated and bounded on every read: a corrupt, oversized or duplicate-id file fails closed to the supplied presets, is named once in the panel status and again in the editor, and the store stamps a new revision on every save so job provenance keeps distinguishing edited generations.

Native checks to run: open **Edit preset…** from a supplied preset and save a named copy; restart Rhino to see the restored default; edit the personal preset in place (revision advances) and delete it (confirmed, selection falls back); optionally place a corrupt file in the presets folder and check the honest failure. The editor's dialog layout at narrow widths, Windows paths and the registered dock remain acceptance cases.

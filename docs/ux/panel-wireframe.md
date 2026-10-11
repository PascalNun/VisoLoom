# Low-fidelity panel wireframes

Status: accepted interaction target. Native capture-test panel is implemented; visual and usability validation in a supported host remains open. Use native Eto widgets and Rhino theme metrics. Width is illustrative; test roughly 300–420 logical pixels, high-DPI display scaling, dark/light themes and keyboard navigation.

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

Source/Result toggling must keep the same display scale and framing. Compare can provide a simple overlay/split later; first implementation needs at least reliable side-by-side or toggle review. Fit and 100% are image inspection, not camera controls. The image region can expand without taking over Rhino's workspace.

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

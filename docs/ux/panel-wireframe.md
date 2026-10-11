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
│ Mode       [Competition Sketch    ▾] │
│                                      │
│ Prompt (optional)                    │
│ ┌──────────────────────────────────┐ │
│ │ Warm grey wash, quiet atmosphere │ │
│ └──────────────────────────────────┘ │
│                                      │
│ Resolution [2× Viewport            ▾] │
│            3840 × 2160 px             │
│                                      │
│ Office server · configured model     │
│ Project-approved destination  Change │
│                                      │
│ ▸ Advanced                           │
│                         [ Render ]   │
└──────────────────────────────────────┘
```

The destination/model row is illustrative, not a claim that an office engine exists. View label follows Rhino; if Named View association is uncertain, use Current view plus viewport name. No VisoLoom camera chooser or material library.

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

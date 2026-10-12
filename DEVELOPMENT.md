# Developing VisoLoom

## Quick test on your Mac

Double-click **Test VisoLoom.command** in this checkout. It builds the current code, installs a local development copy for Rhino 8, starts the authenticated reference service and opens Rhino. The plug-in connects automatically through a local handoff restricted to your OS user. No token is copied, displayed or saved in a file.

1. If Rhino was already open, save your work and restart it to load the new build. Rhino cannot replace a loaded .NET plug-in in the same session.
2. Run `VisoLoom` and wait for **Connected to the local test service**. This check sends no scene; **Change…** remains available for a deliberate manual connection.
3. Choose a supported model view and click **Preview capture**. Review the locally prepared image; **Enlarge preview…** opens a larger view. Then click **Test capture**. Source and result should match. Try **Save PNG…**.
4. Keep the launcher window open while testing. Press **Control-C** there when finished. It stops only its own local service and closes the automatic handoff.

After a code change, stop the service and run the launcher again, then restart Rhino. Each launcher session has a new token; the plug-in obtains it automatically. Restarting just the service, without changing plug-in code, does not require a Rhino restart or token paste. It rebuilds automatically. The local capture-test choice returns the image unchanged. OpenAI is a separate, explicitly configured choice.

The launcher uses the existing isolated development SDK when available, then the SDK on your path. `VISOLOOM_DOTNET` can select a different `dotnet` executable deliberately. It never downloads tools or changes Rhino's application/runtime. If another service occupies the port, it stops with a message instead of replacing that process.

The local development copy lives in `~/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns/VisoLoom-dev/VisoLoom.rhp/`, following [McNeel's Mac plug-in discovery layout](https://developer.rhino3d.com/guides/rhinocommon/plugin-installers-mac/). The launcher refreshes its six build files there; an existing folder without its development marker is left alone. To remove this development installation, quit Rhino and remove only the `VisoLoom-dev` folder. This is a local development setup, not a public installer or Yak package.

Test-service captures use `~/Library/Application Support/VisoLoom/development-server`, with the service's usual 24-hour retention. Rhino history remains in its existing per-user history location. Neither is saved in Git or `.3dm`; exported PNGs stay wherever you choose to save them.

For automated checks without installing or opening Rhino:

```sh
./"Test VisoLoom.command" --check
```

For service-startup checks without opening Rhino, `--headless` builds and installs the development copy, verifies authenticated readiness and waits until stopped. Native panel/capture acceptance is a separate step; a ready local service does not prove it.

## First OpenAI render

Save your Rhino work, stop the old launcher with Control-C, run **Test VisoLoom.command** again and restart Rhino to load the new assembly. The launcher does not replace an already running service or a loaded plug-in.

1. Open **VisoLoom → Settings…**, or the **VisoLoom** page in Rhino Options/Preferences.
2. Enter your own OpenAI API key in the password field. Do not paste it into chat or project files. Enable **Allow rendering with OpenAI**. Names/descriptions are optional and off by default.
3. Choose **Remember API key** if you want OS persistence; otherwise it lasts for this Rhino session. Click **Check and save connection**. This checks access to the pinned image model without uploading a capture or generating an image. The page's button commits the connection explicitly.
4. Choose **OpenAI · Sunburst** in the panel's **Model** menu, then a preset such as **Photorealistic** or **Competition Sketch** and an optional prompt. Changing these selections sends nothing.
5. Use a non-confidential, simple architectural fixture in a supported display mode, inspect the dimensions/destination, then click **Preview capture**. Review the image and **Review prepared prompt…** before clicking **Render**. API image generation is separately billed. Compare the result with the captured geometry and save it.

One request sends one PNG plus the composed preset/prompt and bounded camera, frame, units, clipping and material context. The full `.3dm`, object geometry, GUIDs, texture files and local paths are not sent. Optional names can still identify a project; text typed into the prompt and visible lettering in the image are also disclosed. **Review prepared prompt…** shows the frozen request text before submission. Preview is local, needs no key/service and writes no history. Render uses the reviewed PNG and prompt without recapturing. View, scene, size or intent changes require **Refresh preview**; another render also requires a refreshed preview. Inspecting a preview creates no billable request. See [ADR 0015](docs/adr/0015-preview-before-submission.md).

The first model is pinned to `gpt-image-2.5-sunburst-2026-09-08`, one output at medium quality. Unsupported sizes fail preflight. When required, the image codec fits the complete frame into a supported canvas, then removes only recorded surrounding padding and restores the requested size. The native canvas and experimental high-resolution warning appear in the panel. Both the native result and normalized PNG remain in private local history with hashes and processing provenance.

**Stop waiting** ends local waiting; OpenAI may still finish and charge the request. There is no automatic retry or cloud reconnect that would submit another image. If completion is unconfirmed, check the account before deliberately trying again.

Remembering a key uses macOS Keychain or Windows Credential Manager. If first-time saving fails, the page reports session-only use explicitly; no plaintext fallback is written. Changing a previously remembered key requires successful OS storage/deletion. **Disconnect and forget key** disables new submissions and removes the saved entry; a removal failure is reported. Nonsecret preferences live in Rhino's user settings, never the model file.

**Current evidence:** 80 automated tests pass locally; OpenAI tests use simulated responses, with no paid requests. User screenshots confirm the Mac Settings flow, model discovery and remembered key. The first odd-size render exposed `ImageAttributes.SetWrapMode`, which Rhino's Mac drawing library does not implement. An isolated probe reproduced that missing method before submission; the corrected processor compiles directly against the installed Mac library. The user subsequently completed a real edit and exported its PNG. Matching local provenance confirms a 2358 × 1810 capture, a 2368 × 1824 native OpenAI output, removal of only 5-pixel side and 7-pixel top/bottom padding, scale 1, and a 2358 × 1810 export identical to the saved result. This validates the Mac padding path; resampling and Windows credential persistence remain acceptance checks. Codec failures now distinguish preparation before upload from normalization after a potentially billed result.

OpenAI now documents account-based [Sign in with ChatGPT](https://developers.openai.com/siwc/quickstart), but its current plan-usage route explicitly [excludes image generation](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations). It cannot replace the API key for this render adapter yet. Reassess supported login if image editing becomes eligible; do not reuse consumer browser sessions.

## Test action in the desktop app

In the app's **Settings → Local environments**, select VisoLoom and add an action named **Test VisoLoom** with this macOS script:

```sh
./"Test VisoLoom.command"
```

Use the action's script field, not the worktree setup script. Leave setup empty for this workflow: the launcher already builds when you deliberately run it. Do not copy the launcher's contents into settings. Calling the checked-in file keeps the shortcut current as the code changes. The action appears in the project top bar and runs in the integrated terminal; keep that terminal open during testing and press Control-C there to stop the service. Restart Rhino after a changed plug-in build; the local connection is automatic. See [OpenAI's local-environment documentation](https://learn.chatgpt.com/docs/environments/local-environment).

For an optional **Check VisoLoom** action that builds and runs automated checks without opening Rhino, use `./"Test VisoLoom.command" --check`.

## Requirements

- .NET SDK **10.0.201** (pinned in `global.json`) and a serviced **.NET 8 runtime** for Core tests. The server targets .NET 10; plug-in and shared libraries target .NET 7 for Rhino's .NET 7 and .NET 8 hosts.
- **Rhino 8**, using its default .NET runtime, for host testing. RhinoCommon **8.0.23304.9001**, the earliest stable Rhino 8 SDK, is the pinned build reference. Windows' optional legacy .NET Framework mode is outside this build. An SDK compile is not evidence of graphics/capture compatibility.
- Mac Apple Silicon is the primary development target; Windows CI builds and runs the independent tests as well.

This workstation has Rhino 8.16, with user-confirmed native capture, automatic connection and PNG saving. It has not been updated or modified by this task. An isolated development toolchain with SDK 10.0.201 and runtime 8.0.31 was installed under `~/.local/share/visoloom-dev/dotnet`; invoke its `dotnet` when the system installation lacks .NET 8. It does not change Rhino's runtime.

```sh
dotnet restore VisoLoom.sln --locked-mode
dotnet build VisoLoom.sln --configuration Release --no-restore
dotnet test VisoLoom.sln --configuration Release --no-build
```

On this workstation substitute `~/.local/share/visoloom-dev/dotnet/dotnet` for `dotnet`. NuGet sources, package versions, transitive hashes, compiler settings and workflow action commits are pinned. Review the SDK/runtime servicing baseline before distribution, especially the .NET 8 support horizon. Do not copy host assemblies or a standalone drawing runtime into the plug-in folder.

## Start the reference server

The development service listens only on `http://127.0.0.1:5179`. It requires a fresh, random session token. It does not run an image model. It rejects browser Origin requests, unexpected Host headers and unauthenticated requests. Logging deliberately excludes credentials, payloads and filesystem paths.

In a local macOS terminal:

```sh
export VISOLOOM_REFERENCE_TOKEN="$(openssl rand -hex 32)"
umask 077
export VISOLOOM_REFERENCE_AUTOCONNECT=1
dotnet run --project server/VisoLoom.ReferenceServer --configuration Release --no-build
```

The updated plug-in connects automatically. Stop the server with Ctrl-C, then `unset VISOLOOM_REFERENCE_TOKEN VISOLOOM_REFERENCE_AUTOCONNECT` in that terminal. A new token is picked up on the next capture. Omit `VISOLOOM_REFERENCE_AUTOCONNECT` to keep the handoff disabled and use **Change… → Session token** manually; an explicit manual connection is never replaced automatically.

PowerShell 7 on Windows:

```powershell
$env:VISOLOOM_REFERENCE_TOKEN = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:VISOLOOM_REFERENCE_AUTOCONNECT = "1"
dotnet run --project server/VisoLoom.ReferenceServer --configuration Release --no-build
```

The token lives in process memory and the parent terminal environment, not a config file, clipboard, Scene Package or `.3dm`. Automatic development connections use a bounded named-pipe handoff with same-OS-user checks at both ends; HTTP authentication remains mandatory. See [ADR 0011](docs/adr/0011-automatic-local-development-connection.md). The temporary local token is never persisted. OpenAI has a separate optional OS credential store; see the setup below. The default server store is `Environment.SpecialFolder.LocalApplicationData/VisoLoom/reference-server`; `VISOLOOM_REFERENCE_STORAGE` can specify a deliberate absolute development directory. One process holds an exclusive storage lock. The server keeps jobs for 24 hours and expired-ID tombstones for another 24 hours. Do not expose this service as an office inference server.

## Load and exercise the plug-in

On Mac, use the launcher above to create the required `.rhp` folder and restart Rhino. On Windows, build Release and load `src/VisoLoom.Plugin/bin/Release/net7.0/VisoLoom.rhp` using Rhino's PlugInManager install/load UI. Keep the adjacent `VisoLoom.Core.dll`, `VisoLoom.Protocol.dll`, `VisoLoom.Providers.dll`, dependency manifest and runtime configuration with it. The folder must not contain RhinoCommon, Eto, Rhino.UI or System.Drawing.Common assemblies. No public installer or Yak package has been released.

Run `VisoLoom` to open the native panel. Choose a saved **Named view** and click **Show named view in Rhino**, or use the active/open model viewport. Use its intended native display style, wait for the automatic local connection, select a resolution, **Preview capture**, inspect it, then **Test capture**. Source and result should show the same image, with a successful hash check. **Save PNG…** exports it using the ordinary save dialog. `VisoLoomRender` submits the current preview with the document session's panel settings; it asks for a fresh preview if none is current.

Capture supports viewport, 2×, 4× and custom sizes within 16,777,216 pixels. Custom width and height stay linked and round to the nearest whole-pixel pair with the view's exact proportions. For example, a 2358 × 1810 view can capture at 1179 × 905 or 2358 × 1810; entering 1920 as its width resolves visibly to 2358. No cropping or stretching occurs.

Named Views remain Rhino's saved compositions. Selection sends nothing and changes nothing. **Show named view in Rhino** explicitly restores the selected view into the active model viewport. Capture uses that visible viewport's current frame/aspect, display style and clipping, rather than an independent offscreen saved render size. Moving the camera, resizing its viewport or replacing/renaming/deleting the saved view requires showing it again or choosing Active viewport. Display-style changes are allowed. A pinned open viewport can also be captured without activating it.

Native custom display styles are enabled; the user's Rendered_LN_PN AO/edge style has authored-fixture evidence on this Mac. Two-point perspective capture is enabled after a native current-view framing check. Page/detail views, Raytraced and other live render engines remain unsupported. Wider texture, transparency and lighting checks are still required. The panel shows capture/export size, model canvas and any resized model content before Render; unsupported plans disable the action.

**Reconnect** is offered after an ambiguous connection loss. It reuses the saved capture and its idempotency key; a changed request is rejected instead of silently creating another job. This recovery lasts for the current Rhino session. Reopening persisted history after restarting Rhino is deferred.

Local history is stored outside `.3dm` in `LocalApplicationData/VisoLoom/history`, retaining the latest 20 captures. It includes source PNG, the exact prepared model-reference PNG and its hash, scene manifest, intent/plan and completed result/provenance. Preview alone remains in memory and writes none of these files. Exported PNGs are never cleanup targets. The full scene manifest stays in local history. OpenAI receives the smaller recorded prompt context and reference image only for a cloud render; names/descriptions are optional. These fields and the visible image can still be confidential. macOS directories/files use owner-only modes; Windows uses the user's profile ACL inheritance, pending deployment-level ACL validation.

## Verification and release gates

Tests use authored synthetic RGB fixtures, no confidential models or provider credentials. The integration suite also checks automatic credential handoff, token rotation, malformed/truncated handoffs, timeout and cancellation. The HTTP suite starts real ephemeral loopback services and checks round trip, idempotency/reconnect, auth/origin/Host rejection, queue limits, cancellation, restart, expiry and malformed uploads. CI builds the plug-in and runs tests on macOS and Windows with read-only repository permissions.

The compatibility correction also passed an isolated client round trip and idempotent replay under Rhino 8.16's bundled .NET 7.0.0 runtime. This exercises the shared client libraries, not Rhino's panel, capture or drawing codec. The plug-in source compiles against both the original 8.0 SDK and the installed 8.16 SDK. See [ADR 0010](docs/adr/0010-rhino-8-series-compatibility.md) for the support target and remaining matrix.

Follow the [host smoke-test checklist](docs/development/plugin-smoke-test.md) before claiming capture or UX acceptance. User screenshots confirm Mac Wireframe and standard Rendered perspective round trips at 2358 × 1810 pixels, with both previews and reported byte equality. The user confirmed automatic connection without token entry, and the latest screenshot shows corrected label/button layout at its panel width. Normal PNG saving is also user-confirmed. Authored native checks now cover perspective/parallel capture at viewport and 2×, standard Rendered and custom Rendered_LN_PN, inactive-view capture, view-specific hidden clipping metadata, explicit Named View restore, stale source rejection and selected state/undo invariants. Still needed: service-only restart with Rhino kept open, save cancellation/overwrite/error cases, the revised panel at narrow widths, broader Mac/Windows host behavior, full appearance/material coverage and slow/disk-full/closed-document UX. A compiled panel is not a usability review. See the [implementation review](docs/development/phase-4-review.md) for current evidence and the next milestone.


### Repeatable native capture check on Mac

After loading the latest build, run Rhino's **RunPythonScript** with [tests/native/check-capture.py](tests/native/check-capture.py). It opens its own small authored fixture in a separate document window and makes no provider request. It checks active/inactive perspective and Top captures at viewport and 2×, a hidden clipping plane, the custom style when available, two-point capture, Named View selection/explicit restore and stale sources. It compares camera/frame, selection, table counts, modified state and undo counters around capture.

The script prints a temporary folder containing PNGs and a report. Close its **VisoLoom-capture-fixture.3dm** window without saving when finished. Do not run the fixture-opening workflow on Windows: Rhino's file-open behavior there would replace the current document. This check is separate from automated build tests and from a full appearance/fidelity benchmark.

### Two-point framing check

Two-point capture uses the existing native projection. To check the intended view without converting it, use Rhino's `RunPythonScript` with `tests/native/check-two-point.py`. It captures the current two-point model view locally at viewport size and another exact-aspect size within the capture limits. It reads native pipeline camera/frustum and projected landmarks, checks PNG dimensions and compares source camera, selection, table counts, modified state and undo counters before/after. It does not open a fixture, change the view, read credentials or call a provider. PNGs and the report stay in a private temporary folder, outside Git.

The native check passed in the user's two-point view at 2358 × 1810 and 1179 × 905 on Rhino 8.16 / .NET 7. Source state was unchanged; 25 projected landmarks matched capture pixels within 7e-12 pixels. Numerical frustum rounding uses a strict tolerance rather than exact floating-point equality. The revised source harness passed all 14 authored captures, including restoration of a saved two-point view from ordinary perspective and rejection of a later projection change. Native Windows acceptance and full revised panel interaction after restart remain pending. See [ADR 0014](docs/adr/0014-two-point-perspective-capture.md).

### Preview check

Run [tests/native/check-preview.py](tests/native/check-preview.py) with RunPythonScript after loading the latest build. This Mac-only authored fixture checks local and OpenAI preparation without network/key access or history writes, unchanged fixture state, and expiration after prompt, camera, geometry, display-style and Named View changes. It reports original-window state separately around fixture opening and around the preview checks, since opening a Mac document can initialize other window frames. Close its fixture without saving afterwards. Full loaded-panel interaction remains a separate acceptance check.

Preview/context source commit `f95435c` also passed [macOS and Windows CI](https://github.com/PascalNun/VisoLoom/actions/runs/38307820610), including all 80 tests and deployment checks. The development copy is installed; save your work and restart Rhino to load it. Loaded-panel preview inspection remains a separate native acceptance check.

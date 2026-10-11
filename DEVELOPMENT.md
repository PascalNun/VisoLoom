# Developing VisoLoom

## Requirements

- .NET SDK **10.0.201** (pinned in `global.json`) and a serviced **.NET 8 runtime** for Core tests. The server targets .NET 10; plug-in and shared libraries target .NET 7 for Rhino's .NET 7 and .NET 8 hosts.
- **Rhino 8**, using its default .NET runtime, for host testing. RhinoCommon **8.0.23304.9001**, the earliest stable Rhino 8 SDK, is the pinned build reference. Windows' optional legacy .NET Framework mode is outside this build. An SDK compile is not evidence of graphics/capture compatibility.
- Mac Apple Silicon is the primary development target; Windows CI builds and runs the independent tests as well.

This workstation has Rhino 8.16, now a candidate for native testing without a Rhino update. It has not been updated or modified by this task. An isolated development toolchain with SDK 10.0.201 and runtime 8.0.31 was installed under `~/.local/share/visoloom-dev/dotnet`; invoke its `dotnet` when the system installation lacks .NET 8. It does not change Rhino's runtime.

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
printf '%s' "$VISOLOOM_REFERENCE_TOKEN" | pbcopy
dotnet run --project server/VisoLoom.ReferenceServer --configuration Release --no-build
```

Paste the token into the plug-in's **Change… → Session token** field. The shell command copies it without printing it or saving it in a file. Clear the clipboard after pasting. Stop the server with Ctrl-C, then `unset VISOLOOM_REFERENCE_TOKEN` in that terminal. A restart may use a new token; update the panel connection for that session.

PowerShell 7 on Windows:

```powershell
$env:VISOLOOM_REFERENCE_TOKEN = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
Set-Clipboard $env:VISOLOOM_REFERENCE_TOKEN
dotnet run --project server/VisoLoom.ReferenceServer --configuration Release --no-build
```

The token lives in process memory and the parent terminal environment, not a config file, Scene Package or `.3dm`. OS credential persistence is not implemented. The default server store is `Environment.SpecialFolder.LocalApplicationData/VisoLoom/reference-server`; `VISOLOOM_REFERENCE_STORAGE` can specify a deliberate absolute development directory. One process holds an exclusive storage lock. The server keeps jobs for 24 hours and expired-ID tombstones for another 24 hours. Do not expose this service as an office inference server.

## Load and exercise the plug-in

Build Release and load `src/VisoLoom.Plugin/bin/Release/net7.0/VisoLoom.rhp` using Rhino's PlugInManager install/load UI. Keep the adjacent `VisoLoom.Core.dll`, `VisoLoom.Protocol.dll`, `VisoLoom.Providers.dll`, dependency manifest and runtime configuration with it. The folder must not contain RhinoCommon, Eto, Rhino.UI or System.Drawing.Common assemblies. No installer or Yak package has been released.

Run `VisoLoom` to open the native panel. In a model viewport choose Wireframe, Shaded or Rendered, use **Change…** to enter the loopback server and session token, select a resolution, then **Test capture**. Source and result should show the same image, with a successful hash check. **Save PNG…** exports it using the ordinary save dialog. `VisoLoomRender` submits the same action with the document session's panel settings.

The first slice supports viewport, 2×, 4× and a custom exact-aspect size within 16,777,216 pixels. No camera resize/crop occurs. Two-point perspective, page/detail views and other/custom display modes are deliberately rejected until validated. If a selected size exceeds server limits, choose a smaller one; no silent downscale occurs.

**Reconnect** is offered after an ambiguous connection loss. It reuses the saved capture and its idempotency key; a changed request is rejected instead of silently creating another job. This recovery lasts for the current Rhino session. Reopening persisted history after restarting Rhino is deferred.

Local history is stored outside `.3dm` in `LocalApplicationData/VisoLoom/history`, retaining the latest 20 captures. It includes source PNG, scene manifest, intent/plan and completed result/provenance. Exported PNGs are never cleanup targets. Layer/material names can be confidential; nothing leaves literal loopback in this slice. macOS directories/files use owner-only modes; Windows uses the user's profile ACL inheritance, pending deployment-level ACL validation.

## Verification and release gates

Tests use authored synthetic RGB fixtures, no confidential models or provider credentials. The HTTP suite starts real ephemeral loopback services and checks round trip, idempotency/reconnect, auth/origin/Host rejection, queue limits, cancellation, restart, expiry and malformed uploads. CI builds the plug-in and runs tests on macOS and Windows with read-only repository permissions.

The compatibility correction also passed an isolated client round trip and idempotent replay under Rhino 8.16's bundled .NET 7.0.0 runtime. This exercises the shared client libraries, not Rhino's panel, capture or drawing codec. The plug-in source compiles against both the original 8.0 SDK and the installed 8.16 SDK. See [ADR 0010](docs/adr/0010-rhino-8-series-compatibility.md) for the support target and remaining matrix.

Follow the [host smoke-test checklist](docs/development/plugin-smoke-test.md) before claiming capture or UX acceptance. Still needed: actual Mac/Windows host behavior, high-DPI pixel dimensions, graphics-driver/display-mode behavior, no model mutation, slow/disk-full/closed-document UX, clipping and material coverage. A compiled panel is not a usability review. See the [implementation review](docs/development/phase-4-review.md) for current evidence and the next milestone.

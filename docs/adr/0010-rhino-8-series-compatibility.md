# ADR 0010 — Rhino 8 series compatibility

Status: implements the user's clarified compatibility target, 2026-10-11. Supersedes the SDK/runtime baseline in ADRs 0001 and 0009.

## Context

The user expects someone with Rhino 8 to use VisoLoom without checking its service-release number. The earlier 8.21 minimum was an implementation choice, not a requirement of the current APIs. Compilation against the original Rhino 8 SDK confirms that the first slice does not need later APIs.

## Decision

Target the whole Rhino 8 service-release series on macOS and Windows using Rhino's default .NET runtime. Compile the plug-in and its shared libraries as managed AnyCPU `net7.0`, using stable RhinoCommon **8.0.23304.9001**. This provides a common assembly baseline for Rhino 8's original .NET 7 and later .NET 8 hosts. The independent server remains `net10.0`; tests use .NET 8 and .NET 10. No runtime is bundled or changed in Rhino.

Remove the 8.21 minor-version gate. The major-version check targets Rhino 8; other major versions are outside the current support target. Future features needing a newer API should detect availability and explain that feature's requirement while keeping the basic workflow available where practical. Do not raise the whole plug-in's minimum without a demonstrated need.

The original SDK's NuGet assembly metadata is `net48`. Suppress its expected NU1701 warning only on the RhinoCommon reference. Reference System.Drawing.Common 7.0.0 explicitly for compilation; Rhino supplies the drawing runtime. Neither SDK nor drawing assemblies may be copied into the plug-in output. Windows' optional legacy .NET Framework runtime is not supported by this assembly and would require a separate target.

## Evidence and remaining validation

- Release solution build and 36 tests pass locally and in [macOS/Windows CI](https://github.com/PascalNun/VisoLoom/actions/runs/38155085522) with the revised targets.
- All plug-in source compiles against both the original 8.0 SDK and the installed 8.16 SDK.
- Discovery, unchanged PNG round trip and idempotent replay pass under Rhino 8.16's actual bundled .NET 7.0.0 runtime, without opening or modifying Rhino.
- Native panel, capture, drawing codec and document behavior remain untested. Compilation and a detached client probe are not Rhino acceptance evidence.

Validate the earliest available Rhino 8 host, installed 8.16 and current Rhino 8 on Apple Silicon and Windows. Record exact builds and runtimes in the [host checklist](../development/plugin-smoke-test.md). The series is a support target; exhaustive service-release validation is not claimed. Runtime servicing is supplied by Rhino and remains a release consideration, separate from the assembly's compatibility target.

McNeel documents Rhino 8's original .NET 7 baseline and later .NET 8 transition in its [runtime guidance](https://developer.rhino3d.com/guides/rhinocommon/moving-to-dotnet-core/). The transition details and runtime-selection commands differ by platform; no runtime-switch instruction is required by this change.

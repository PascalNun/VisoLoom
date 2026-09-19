# Development status and proposed next steps

This is a Phase 0 documentation workspace. There is no solution, build command, automated test suite, CI workflow, plugin binary or local service yet. Commands below are limited to read-only environment inspection; production scaffolding awaits the brief's architecture confirmation.

## Environment inspected on 2026-09-19

| Item | Observed |
| --- | --- |
| Architecture | Apple Silicon / arm64 |
| Rhino | 8.16, build 8.16.25042.13002 |
| Standalone SDK | .NET 10.0.201 |
| Standalone runtimes | Microsoft.NETCore.App and Microsoft.AspNetCore.App 10.0.5 |
| Rhino bundled files | .NET 7.0.0 arm64/x86_64 runtime files present; active runtime unverified |
| GitHub CLI | Installed; account/owner/permissions not inspected for this phase |
| Workspace | Initially empty; no existing Git repository |

Checks used: `uname -m`, `dotnet --list-sdks`, `dotnet --list-runtimes`, Rhino application Info.plist reads, and targeted installed RhinoCommon XML symbol inspection. No runtime install, application update or network upload of project content was performed.

## Proposed baseline

Rhino 8.20+ running .NET 8, managed AnyCPU plug-in/shared libraries, independent ASP.NET Core service on .NET 10. Use a current supported patch at implementation time. The installed .NET SDK alone does not prove Rhino runtime compatibility. Confirm the desired host update rather than modifying the user's Rhino installation during this review. [McNeel setup/runtime guidance](https://developer.rhino3d.com/guides/rhinocommon/moving-to-dotnet-core/).

Use VS Code or another supported C# development environment on Mac, and a supported C# toolchain on Windows. Preserve host-provided Rhino/Eto assemblies rather than copying them into deployment output. Exact NuGet versions, compiler/language level and lockfiles follow the compatibility spike and license audit.

## After architecture approval

1. Finalize the draft schemas, protocol version rules and fixture expectations. Accept/update the ADRs.
2. Establish the compatible development host. Initialize Git and create private `VisoLoom` under the confirmed authenticated account/organization. Verify visibility. No `LICENSE`, public upload or Yak push.
3. Create the proposed small solution and pinned dependencies; add solution build instructions that have actually been run, contributor conventions and locked restore.
4. Implement pure Core behavior and meaningful tests before host/provider complexity.
5. Implement reference server and local adapter with HTTP integration tests; no inference runtime.
6. Implement current-view capture and native result UI after the UX design, then test in both supported Rhino hosts.
7. Record Phase 4 results as working/stubbed/unverified, with artifacts, limits and next real-provider benchmark.

## Test and CI plan

Core tests cover schema version rejection/additive compatibility, serialization and unknown values, camera/frame invariants, resolution transforms and limits, mapping precedence/renames, prompt layers, policy/provider selection, and job transitions. Use public/synthetic fixtures, not confidential project files.

Protocol tests exercise actual loopback HTTP: capabilities, multipart validation, limits, auth failure, idempotent duplicate and conflicting submit, cancellation/completion race, reconnect after lost submit response, restart behavior, missing/expired output and byte-identical PNG return. No billable cloud API calls in CI.

CI should compile the plugin against a pinned SDK and run host-independent Core tests on .NET 8, with protocol/service integration on .NET 10. Use Windows/macOS runners for platform-independent behavior and a Linux leg if useful for the neutral server. Pin workflow actions to reviewed commits, use read-only default permissions, and upload only synthetic test artifacts. A hosted build does not prove licensed Rhino execution, Apple Silicon GPU capture, Keychain behavior, or Windows UI behavior.

In-Rhino acceptance is a separate manual/controlled integration matrix covering the [feasibility experiments](docs/development/rhino-feasibility.md), release-minimum and current Rhino 8, both operating systems, and high-DPI display. Report missing platform coverage honestly. No tests have been run for production behavior in Phase 0 because there is no production code.

## Delivery constraints

Build a local Yak prerelease only after host validation; encode the tested minimum service release/platform and inspect archive contents. Keep model runtimes, secrets, scenes, provider outputs and host assemblies out of Git and deployment output. Public publishing remains a separate user instruction.

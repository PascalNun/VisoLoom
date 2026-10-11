# ADR 0001 — Standard plug-in and runtime baseline

Status: Accepted by the user on 2026-09-19.

The runtime and minimum-service-release portions below are historical and superseded by [ADR 0010](0010-rhino-8-series-compatibility.md), following the user's clarified Rhino 8 compatibility target on 2026-10-11. The standard plug-in, native panel and independent server decisions remain in force.

## Context

VisoLoom needs current-view capture and native interaction. It does not initially evaluate a physical light-transport scene or replace Rhino's selected renderer. A full render plug-in adds renderer lifecycle and content integration without automatically providing aligned auxiliary passes.

## Decision

Use a standard RhinoCommon plug-in with commands, a registered Eto panel, and display/capture APIs. Require Rhino 8.21+ with .NET 8 selected, `net8.0`, managed AnyCPU. The independent reference service targets .NET 10. The build pins RhinoCommon 8.21.25188.17001, the next stable package after the 8.20 prerelease-only NuGet series. Compilation has passed; in-host compatibility remains a release gate.

## Alternatives

`RenderPlugIn` would integrate with Rhino's Render selection and settings, but introduces responsibilities not needed by the first slice. A standalone web UI would duplicate interaction and add a browser runtime boundary. Targeting all Rhino 8 runtimes via `net48`/`net7.0` adds a compatibility matrix without a stated deployment need.

## Consequences and revisit

The installed Rhino 8.16 needs a deliberate development-environment change for the recommended baseline. Compilation does not prove capture compatibility. Revisit for a demonstrated need for Rhino RenderWindow/renderer integration, an office's older-host requirement, or McNeel runtime changes. Review the .NET 8 servicing horizon before release. Evidence: [Rhino feasibility](../development/rhino-feasibility.md).

# ADR 0001 — Standard plug-in and runtime baseline

Status: Accepted by the user on 2026-09-19.

## Context

VisoLoom needs current-view capture and native interaction. It does not initially evaluate a physical light-transport scene or replace Rhino's selected renderer. A full render plug-in adds renderer lifecycle and content integration without automatically providing aligned auxiliary passes.

## Decision

Use a standard RhinoCommon plug-in with commands, a registered Eto panel, and display/capture APIs. Propose Rhino 8.20+ with .NET 8 selected, `net8.0`, managed AnyCPU. The independent reference service targets .NET 10. SDK versions are pinned after the approved compatibility spike.

## Alternatives

`RenderPlugIn` would integrate with Rhino's Render selection and settings, but introduces responsibilities not needed by the first slice. A standalone web UI would duplicate interaction and add a browser runtime boundary. Targeting all Rhino 8 runtimes via `net48`/`net7.0` adds a compatibility matrix without a stated deployment need.

## Consequences and revisit

The installed Rhino 8.16 needs a deliberate development-environment change for the recommended baseline. Compilation does not prove capture compatibility. Revisit for a demonstrated need for Rhino RenderWindow/renderer integration, an office's older-host requirement, or McNeel runtime changes. Review the .NET 8 servicing horizon before release. Evidence: [Rhino feasibility](../development/rhino-feasibility.md).

# 0011 — Automatic local development connection

Status: implemented within the user's authorized local-testing scope, 2026-10-11. Refines the development setup in ADR 0005; real provider credential storage remains separate.

## Problem

Pasting a new local-service token after each launch interrupts testing and makes an internal credential look like an image-model setting. The reference service should connect automatically while preserving its authentication and loopback restrictions.

## Decision

The test launcher generates a random 64-character hexadecimal bearer token in its process environment and explicitly enables `VISOLOOM_REFERENCE_AUTOCONNECT=1`. The service opens the versioned local named pipe `VisoLoom-development-v1`. Both server and client use `PipeOptions.CurrentUserOnly`; the client connects to this computer only. The Mac launcher sets `umask 077` before opening the Unix socket. Ordinary server startup leaves this handoff disabled unless the flag is explicitly set.

The fixed 66-byte payload contains a valid port and the token. The client constructs a literal `http://127.0.0.1` endpoint, validates it through the existing endpoint policy, and checks authenticated capabilities before marking the panel connected. No HTTP bootstrap endpoint, browser origin permission, redirect, office destination or unauthenticated mode is added. Discovery sends no scene.

The panel retries missing-service discovery at most once every five seconds, asynchronously. Connection and payload reads have short deadlines. Before a new automatic capture, it obtains the current token again; restarting only the service does not require restarting Rhino. View identity is recorded before discovery and checked again before capture. Reconnect of a saved job must retain its original endpoint. Explicit manual **Change…** connections are never automatically replaced.

Tokens are held in memory and the launcher's environment, never in command-line arguments, clipboard, history, `.3dm`, repository files or a credential handoff file. The launcher removes its environment copy after authenticated readiness, before opening Rhino. Temporary payload buffers are cleared after use and the pipe is disposed on service exit. Changed plug-in assemblies still require one Rhino restart to load them.

## Trust boundary and evidence

This is a same-OS-user development convenience. Other processes running as that user may obtain the token; it does not protect against a compromised account or administrator. It is not office authentication or cloud API-key storage. Predictable pipe naming does not authenticate the application's identity beyond its OS user. No new dependencies are needed.

.NET enforces peer-user checks for `CurrentUserOnly`. Before .NET 11, Unix socket filesystem permissions also depend on the process umask, hence the explicit private launcher setting. [Microsoft's named-pipe permissions documentation](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/namedpipeserverstream-unix-permissions).

The [source CI run](https://github.com/PascalNun/VisoLoom/actions/runs/38167463771) passed all 43 tests on macOS and Windows. Automated tests exercise authenticated discovery/capture, token rotation after restart, absent/stalled services, cancellation and malformed/truncated payload rejection. An isolated process probe using Rhino 8.16’s actual bundled .NET 7.0.0 also passed automatic discovery, authenticated PNG round trip and anonymous rejection, repeated after a fresh-token service restart. This checks the shared client runtime, not Rhino’s UI. On 2026-10-11 the user confirmed automatic connection without token entry in Rhino, with a screenshot showing a successful Rendered perspective round trip and both previews. Cross-account access and service-only restart with Rhino kept open remain host checks; this observation does not establish the full UI acceptance matrix.

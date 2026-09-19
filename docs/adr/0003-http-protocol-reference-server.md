# ADR 0003 — HTTP jobs and reference server

Status: Accepted by the user on 2026-09-19.

## Context

Local, LAN and private-cloud inference should share a contract. Testing transport should not require GPU hardware or a second language/runtime stack.

## Decision

Versioned HTTP/JSON capabilities and asynchronous jobs; multipart scene/PNG upload; polling; explicit cancel action; authorized output download; idempotent submit with durable accepted-job identity. Build a loopback reference service using ASP.NET Core Minimal APIs and shared neutral DTOs. It returns input PNG bytes unchanged and advertises itself as development passthrough.

## Alternatives

FastAPI is appropriate when a Python inference runtime exists, but adds another dependency graph for a mock server. A handwritten HTTP server would reimplement security-sensitive parsing. WebSockets and a broker/database cluster add unnecessary first-slice deployment work. A public ComfyUI workflow schema creates inference-framework coupling.

## Consequences and revisit

The development server is optional and external to Rhino; cloud-only users do not install it. Its queue, restart failure and retention behavior are documented. Actual office inference requires operator authentication, egress controls and durable downstream-job reconciliation. Revisit implementation language when selecting the first local model. Draft: [protocol](../architecture/local-server-protocol.md).

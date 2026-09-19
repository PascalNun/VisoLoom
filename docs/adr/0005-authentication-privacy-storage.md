# ADR 0005 — Authentication, privacy and credential storage

Status: Accepted by the user on 2026-09-19.

## Context

Architectural images and metadata may be confidential. Local storage, output URLs and office servers are trust boundaries as well as cloud providers. Credentials must not enter scene files or logs.

## Decision

Use official API keys/tokens initially; reserve authentication scheme identifiers for future supported OAuth/enterprise flows without implementing speculative login. Provide a narrow credential-store abstraction with macOS Keychain and Windows Credential Manager implementations, scoped by provider/origin/account. Store only credential references in settings. Session-only use is the explicit fallback if OS persistence fails. Do not copy consumer sessions.

Apply a policy at discovery and every network operation: local-only, office-only or approved cloud; allowed origins; minimal field/pass disclosure. HTTP is loopback-only by default. LAN requires authenticated HTTPS. Credentials are never forwarded across origins. Office egress restrictions require operator enforcement, not trust in a capability flag.

Store result/prompt history privately with a bounded retention policy and explicit export. Ordinary logs contain safe correlation/status data; diagnostics never include tokens or signed URLs. No texture bytes or raw geometry upload in this scope.

## Alternatives and consequences

Plaintext config and home-grown encryption are rejected. OS persistence adds native packaging/signing/access-control tests; it must be proven in Rhino on both platforms. OAuth awaits an actual documented provider flow. Secure infrastructure sources: [Apple Keychain](https://developer.apple.com/documentation/security/keychain-services), [Windows CredWrite](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew).

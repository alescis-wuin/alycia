# ADR 0018 — Secure local llama-server session ownership

## Status

Accepted for Lot 10.1.

## Context

The managed llama.cpp adapter already binds `llama-server` to `127.0.0.1`, but the
initial runtime still inherits upstream server defaults that are inappropriate for a
desktop-owned local service: CORS accepts every origin, the bundled Web UI remains
enabled, no API key is configured, and every Alicia process assumes TCP port `8080`.

Loopback binding reduces network exposure but does not by itself prevent a browser
origin from reaching a permissive local HTTP service. A fixed port also creates
collisions between Alicia, another llama.cpp process, or unrelated local software.
The runtime therefore needs an explicit per-process ownership boundary before more
provider features are added.

## Decision

The llama.cpp Infrastructure adapter owns a secure local server session with these
rules:

1. `llama-server` remains bound to IPv4 loopback only.
2. Every Start allocates an available loopback TCP port instead of assuming `8080`.
3. The server command sets `--cors-origins localhost`.
4. The bundled server Web UI is disabled with current upstream `--no-ui` (the successor to `--no-webui`); Alicia is the only UI for
   this managed process.
5. Every server start generates a cryptographically random 256-bit API key.
6. The API key is passed to the child only through llama.cpp's `LLAMA_API_KEY`
   environment variable. It is never added to command-line arguments, provider
   configuration, conversation persistence, log text, or snapshot details.
7. Alicia adds `Authorization: Bearer <key>` to its local llama.cpp HTTP requests,
   for protected readiness ownership checks and chat completion streaming. The public `/health` endpoint remains a readiness signal only.
8. The endpoint and key are runtime-session state. Both are cleared when the managed
   process stops or is observed as exited.
9. Application and Presentation contracts remain unchanged; port selection and local
   authentication are Infrastructure concerns.

The port allocator asks the operating system for an ephemeral loopback port and then
releases the probe socket before starting `llama-server`. There is an unavoidable
small bind race between those operations. Alicia therefore treats `/health` only as
a public readiness signal and then verifies `/props` twice: it must reject an
unauthenticated request with HTTP 401 and accept the session Bearer credential with
HTTP 200. A foreign or unprotected process on the selected port fails closed. More
elaborate reservation/retry behavior belongs to the later Lot 10 timeout/error-classification
work.

## Consequences

- A malicious non-local browser origin no longer receives permissive wildcard CORS
  from the Alicia-managed server.
- The server's own Web UI is not exposed for the managed session.
- Protected llama.cpp endpoints require a per-launch secret known only to Alicia and
  its child process environment; readiness is accepted only after the ownership handshake.
- The secret is not stable and cannot be recovered after a restart, by design.
- Multiple Alicia instances no longer intentionally compete for a single fixed port.
- Provider snapshots can expose the current loopback endpoint without exposing the
  authentication secret.
- Existing provider-neutral Domain, Application, Presentation, and configuration
  schemas do not change.

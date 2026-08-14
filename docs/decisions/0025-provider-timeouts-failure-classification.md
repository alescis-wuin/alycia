# ADR 0025 — Provider timeouts and failure classification

## Status

Accepted for Lot 10.3.

## Context

The managed llama.cpp runtime previously used an infinite shared `HttpClient` timeout, polled readiness without an overall deadline, and surfaced several provider/process/HTTP exception messages directly through Presentation. A failed start could therefore wait indefinitely, raw HTTP bodies or server-log tails could become user-visible, and provider recovery could not distinguish a missing runtime from a network problem or a model failure.

## Decision

Application owns the provider-neutral failure taxonomy:

- `Missing`;
- `Unsupported`;
- `Faulted`;
- `Network`;
- `Model`.

`InferenceProviderException` carries one classification plus an explicitly authored user-safe message. Provider-specific diagnostics may be retained only as the inner exception. `InferenceProviderSnapshot` classifies terminal provider states and permits `Network`/`Model` specialization on `Faulted` snapshots.

Infrastructure keeps the shared `HttpClient` itself unbounded because it is reused by streaming and installer paths, but every latency-sensitive operation owns an explicit finite deadline. llama.cpp readiness has an overall deadline; health/ownership requests, executable probes, installer HTTP headers/download progress, chat response headers, and streaming idle waits are individually bounded. Caller cancellation remains distinct from timeout classification.

Unexpected managed-process exit is detected before accepting an existing session or starting a new response. Startup diagnostics are used only to classify the failure and remain diagnostic-only; log tails, local paths, response bodies, credentials, and raw provider exception messages must not become the top-level user message.

Presentation keeps runtime state and the last recoverable failure separate. If a failed start is followed by successful detection, a healthy runtime can return to `Ready` while retaining `Model`, `Network`, or `Faulted` guidance. `Model` routes the configuration gate to Models, `Network` offers a fresh provider check, and neither classification enables provider installation. A successful explicit provider check or successful start clears the recoverable failure.

## Consequences

- provider operations cannot wait indefinitely at readiness or controlled HTTP boundaries;
- user-facing provider errors are deterministic and do not contain provider diagnostics;
- failed model loading does not masquerade as a missing provider;
- retry policy is still deferred to Lot 10.4: Lot 10.3 bounds and classifies failures but does not automatically retry ambiguous generation requests;
- provider-specific diagnostics remain available to engineering through exception chains and server logs without being projected automatically into the UI.

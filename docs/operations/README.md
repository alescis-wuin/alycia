# Operations

## Local runtime layout

The desktop host resolves `.NET` `Environment.SpecialFolder.LocalApplicationData`, creates an `Alicia` directory, and uses:

```text
Alicia/
├── ui-state.json
├── conversations/
└── providers/
    ├── configuration.json
    └── llama.cpp/
        ├── installation.json
        ├── logs/
        ├── models/
        └── releases/
```

`providers/llama.cpp/models` is also supplied to llama.cpp as `LLAMA_CACHE`.

`ui-state.json` is a versioned Presentation-only document for the explicit history-panel preference, per-conversation scroll mode/offset, and per-conversation visual identity. Stage 10A writes version 2 and still reads version 1, defaulting missing identities without modifying conversation documents. The file is written atomically and remains intentionally separate from conversation persistence.

## Managed llama.cpp lifecycle

The current provider runtime can:

1. detect a managed or external `llama-server`;
2. verify that the executable exposes a CUDA backend;
3. build a managed Linux x64 CUDA server from the official llama.cpp source release;
4. start the selected Hugging Face GGUF model on IPv4 loopback;
5. poll `/health` until the model is ready within a finite readiness deadline;
6. bound provider HTTP/probe waits and classify failures as `Missing`, `Unsupported`, `Faulted`, `Network`, or `Model`;
7. detect unexpected managed-process exit;
8. stream OpenAI-compatible chat-completion SSE responses with bounded response-header and idle waits;
9. stop the complete managed process tree.

Server logs are written below `providers/llama.cpp/logs`. The installation descriptor is `providers/llama.cpp/installation.json`.

## Local server security boundary

The Alicia-managed llama.cpp session is process-owned rather than a fixed localhost service:

- IPv4 loopback binding only;
- a fresh OS-selected loopback port for every start;
- `--cors-origins localhost`;
- bundled llama.cpp UI disabled;
- a cryptographically random 256-bit API key supplied through `LLAMA_API_KEY`, never through process arguments;
- public `/health` used only for readiness;
- `/props` ownership handshake requires 401 without a credential and 200 with the session Bearer credential before Alicia accepts the endpoint;
- chat-completion requests carry the same Bearer credential;
- endpoint and secret are cleared with the managed process session.

The bind-probe socket is released before `llama-server` starts, so a narrow port race remains possible. The ownership handshake fails closed if another local process wins that race. Lot 10.3 bounds readiness/HTTP/probe waits and classifies failures; Lot 10.4 retries only explicitly idempotent transient GET boundaries and never replays generation POSTs automatically.

Credentials must not be copied into repository files or logs. `HF_TOKEN`, when set by the user environment, is inherited for Hugging Face access and is not stored in provider configuration.

## Accessibility motion preference

At Desktop startup Alicia probes a best-effort platform Reduced Motion preference. The Stage 7 thinking indicator becomes static when reduced motion is active. For deterministic testing or an explicit host override:

```bash
ALICIA_REDUCED_MOTION=1 make run
ALICIA_REDUCED_MOTION=0 make run
```

The override affects Presentation motion only; it does not change generation behavior.

## Troubleshooting

### Detect

If detection reports **Missing**, install the managed runtime or make a compatible `llama-server` available on `PATH`.

If detection reports **Unsupported**, verify that `llama-server --list-devices` reports a CUDA device.

### Install

Confirm the CUDA prerequisites before starting a managed build:

```bash
nvidia-smi
nvcc --version
cmake --version
```

Ninja is preferred when available; Make is supported as fallback.

### Start

A valid Hugging Face model reference is required. Alicia reports only a safe model/provider message if startup fails or times out. For engineering diagnosis, the newest file under `providers/llama.cpp/logs` can still be inspected manually; its raw contents are not projected automatically into the UI.

## Deferred operational work

The following remain later-roadmap work:

- distribution and deployment packaging;
- runtime update channels and rollback policy;
- crash reporting;
- product telemetry;
- release signing/package publication.


## Retry policy

Alicia does not retry local model generation automatically. `POST /v1/chat/completions` is a single-attempt operation because a timeout or disconnect can occur after llama-server accepted the request. The Conversation `Retry response` action is the explicit replay boundary.

Automatic retry is limited to idempotent provider HTTP GET boundaries. The llama.cpp adapter retries transient 408/429/500/502/503/504 or network-class failures up to three attempts with a short delay. Permanent HTTP responses are not retried. Release discovery and initial download connection establishment use this policy; an already-started archive stream is not silently restarted. Session ownership probes also use the policy inside the overall readiness deadline. Cancellation immediately aborts pending retry/backoff.

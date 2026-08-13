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

`ui-state.json` is a versioned Presentation-only document for the explicit history-panel preference plus per-conversation scroll mode/offset. It is written atomically and is intentionally separate from conversation documents.

## Managed llama.cpp lifecycle

The current provider runtime can:

1. detect a managed or external `llama-server`;
2. verify that the executable exposes a CUDA backend;
3. build a managed Linux x64 CUDA server from the official llama.cpp source release;
4. start the selected Hugging Face GGUF model on IPv4 loopback;
5. poll `/health` until the model is ready;
6. stream OpenAI-compatible chat-completion SSE responses;
7. stop the complete managed process tree.

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

The bind-probe socket is released before `llama-server` starts, so a narrow port race remains possible. The ownership handshake fails closed if another local process wins that race; retry/timeouts and richer error classification remain Lot 10.3/10.4 work.

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

A valid Hugging Face model reference is required. Inspect the newest file under `providers/llama.cpp/logs` if the process exits before `/health` becomes ready.

## Deferred operational work

The following remain later-roadmap work:

- distribution and deployment packaging;
- runtime update channels and rollback policy;
- crash reporting;
- product telemetry;
- release signing/package publication.

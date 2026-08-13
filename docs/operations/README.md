# Operations

## Local runtime layout

The desktop host resolves `.NET` `Environment.SpecialFolder.LocalApplicationData`, creates an `Alicia` directory, and uses:

```text
Alicia/
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

## Security boundary before Lot 10.1

At checkpoint `c51f1ef`, the process is loopback-only but still inherits permissive upstream CORS, the bundled Web UI, no API key, and a fixed port `8080`. Lot 10.1/10.2 is the P0 hardening slice that must remove these assumptions before further provider expansion.

Credentials must not be copied into repository files or logs. `HF_TOKEN`, when set by the user environment, is inherited for Hugging Face access and is not stored in provider configuration.

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

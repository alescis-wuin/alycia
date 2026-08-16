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
3. build a managed Linux x64 CUDA server from Alicia's validated/pinned llama.cpp release;
4. explicitly check the managed release against Alicia's validated pin and upstream latest;
5. explicitly update an older managed runtime to the validated release while retaining previous release directories;
6. start the selected Hugging Face GGUF model on IPv4 loopback;
7. poll `/health` until the model is ready within a finite readiness deadline;
8. bound provider HTTP/probe waits and classify failures as `Missing`, `Unsupported`, `Faulted`, `Network`, or `Model`;
9. detect unexpected managed-process exit;
10. stream OpenAI-compatible chat-completion SSE responses with bounded response-header and idle waits;
11. stop the complete managed process tree.

Server logs are written below `providers/llama.cpp/logs`. The installation descriptor is `providers/llama.cpp/installation.json`. Lot 10.5 pins the validated release to `b10435` / source commit `9e40df63ba151d771d8b247ac4011cf203337e99`. Candidate releases live under version-specific `releases/<tag>/` directories; `installation.json` is switched atomically only after candidate CUDA validation, and previous release directories are retained until explicit cleanup.

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

## Managed storage maintenance

The Provider workspace exposes **Inspect storage** before any destructive maintenance. Alicia reports runtime bytes, model-cache bytes, and the count of inactive managed release directories. Inspection does not remove files and can run while the local model is active.

Destructive actions require the managed server to be stopped and always open a separate confirmation surface:

- **Clean old releases** removes only inactive Alicia-managed `releases/b<sequence>/` directories and preserves the release selected by `installation.json`, the model cache, provider configuration, and conversations. Unrecognized release-directory names are preserved.
- **Uninstall runtime** removes `installation.json`, `releases/`, `.staging/`, and `logs/`; it deliberately preserves `models/`, `providers/configuration.json`, conversations, UI state, and the legacy `providers/llama.cpp/settings.json` file.
- **Uninstall runtime + cache** removes the same runtime scope plus `providers/llama.cpp/models/`. Model files must then be downloaded again before reuse.

Alicia never stops the provider implicitly to perform these actions. Requested deletion is canonicalized beneath the configured `providers/llama.cpp` root; symbolic links/reparse points are deleted as links and are never traversed. The path stored in `installation.json` is not trusted as a deletion target. Cancellation is honored before mutation starts; after the confirmed bounded deletion begins, Alicia finishes that scope instead of deliberately leaving a half-deleted tree.

## Managed update policy

The Provider workspace exposes **Check update** and, only when applicable, **Update validated**. Update checking compares three distinct values: the active Alicia-managed release from `installation.json`, the release validated and pinned by this Alicia build, and GitHub upstream latest. A newer upstream release is reported but is not executed until a future Alicia build explicitly validates it.

Alicia never auto-updates llama.cpp. **Update validated** is enabled only for a stopped, healthy managed runtime whose installed release is older than the validated pin. A managed release newer than the pin is not downgraded. Update preparation uses the official release tag only to verify its pinned source commit, then downloads the tarball by immutable commit SHA. Cancellation/failure before metadata activation leaves the previous release selected. Old release directories are intentionally retained after update and can now be reclaimed only through the explicit Lot 10.6 managed-storage cleanup action.

## Deferred operational work

The following remain later-roadmap work:

- distribution and deployment packaging;
- crash reporting;
- product telemetry;
- release signing/package publication.


## Retry policy

Alicia does not retry local model generation automatically. `POST /v1/chat/completions` is a single-attempt operation because a timeout or disconnect can occur after llama-server accepted the request. The Conversation `Retry response` action is the explicit replay boundary.

Automatic retry is limited to idempotent provider HTTP GET boundaries. The llama.cpp adapter retries transient 408/429/500/502/503/504 or network-class failures up to three attempts with a short delay. Permanent HTTP responses are not retried. Release discovery and initial download connection establishment use this policy; an already-started archive stream is not silently restarted. Session ownership probes also use the policy inside the overall readiness deadline. Cancellation immediately aborts pending retry/backoff.

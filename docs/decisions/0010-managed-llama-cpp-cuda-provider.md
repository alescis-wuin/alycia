# ADR 0010 — Managed llama.cpp CUDA provider

## Status

Accepted.

## Context

Alicia already owns provider-neutral non-streaming and streaming conversation ports. The first real model provider should therefore remain an Infrastructure concern and must not leak llama.cpp process, HTTP, CUDA, or Hugging Face concepts into the conversation domain.

The initial deployment target for local inference is Linux x64 with an NVIDIA GPU. llama.cpp exposes an OpenAI-compatible streaming server, accepts Hugging Face GGUF repositories through `-hf`, and can be compiled with the CUDA backend through `GGML_CUDA=ON`.

Official Linux release artifacts do not currently provide a ready-to-run CUDA x64 server matching the Windows CUDA bundles. A native Linux CUDA installation therefore requires a local source build.

## Decision

Introduce a provider-management port in Application and a managed llama.cpp CUDA runtime in Infrastructure.

The desktop composition root creates one `LlamaCppProviderRuntime` and injects the same instance into:

- `StreamConversationTurnUseCase` as `IStreamingConversationResponder`;
- Presentation as `IInferenceProviderRuntime` for detect/install/start/stop operations.

The managed runtime follows these rules:

1. Detect a managed `llama-server` first, then a compatible executable from `PATH`.
2. Read build metadata with `--version`, then accept an executable only when `--list-devices` exposes a CUDA device.
3. On Linux x64, install a missing managed runtime by downloading the latest official llama.cpp source release, enabling the `tools` CMake tree required to define `llama-server`, and building the `llama-server` target with `GGML_CUDA=ON`.
4. Never invoke `sudo`, a distribution package manager, or another privileged installer. CMake, a C/C++ compiler, Ninja or Make, the CUDA Toolkit (`nvcc`), an NVIDIA driver (`nvidia-smi`), and the system libraries required by llama.cpp must already be available.
5. Keep the managed runtime, metadata, logs, and Hugging Face model cache under Alicia's platform-local application-data directory.
6. Start the server on loopback only and pass the configured model directly through `-hf owner/model[:quant]`.
7. Let llama.cpp auto-offload model layers with `--n-gpu-layers auto` and use the model's chat template with `--jinja`.
8. Disable automatic multimodal projector download for the current text-only product slice.
9. Wait for `/health` to report readiness before exposing the provider as Running.
10. Stream chat through `/v1/chat/completions` and the existing Application streaming port.
11. Inherit `HF_TOKEN` from the process environment when present; Alicia does not persist or place it on the command line.
12. Report structured installation phases and bounded progress to Presentation, including source-download bytes and Ninja/Make build progress when available.
13. Stop the entire managed server process tree when the user requests Stop or the desktop exits.

## Consequences

- Conversation/Application code remains provider-neutral.
- No provider SDK package is required; the adapter uses the server's local HTTP API.
- llama.cpp installation is user-triggered from the Alicia UI and does not require a manual llama.cpp checkout or build command.
- The initial automatic compiler path is intentionally Linux x64 + NVIDIA CUDA. Other platform installers can be added behind the same Application port.
- Missing system build/CUDA prerequisites are surfaced as actionable provider errors rather than being installed implicitly with elevated privileges.
- Source builds and model downloads can take time; the provider card exposes the current managed-installation phase and progress instead of showing only an indeterminate busy state.
- The current text-only domain does not gain multimodal or provider-specific message types.

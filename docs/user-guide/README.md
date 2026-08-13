# User guide

Alicia currently provides a local desktop conversation workflow backed by an explicitly selected inference provider.

## Conversations

The **Conversations** workspace owns the conversation history and chat surface.

- Create a conversation with the `+` action.
- Select a conversation from history.
- Search history by title and message content.
- Hover a history item to inspect its recent-message preview.
- Rename or delete a conversation from its `…` menu or context menu.
- The history panel can be collapsed for the current session.

## Provider

The **Provider** workspace owns provider lifecycle operations.

For `llama.cpp CUDA`:

1. **Detect** checks for a managed or externally installed CUDA-capable `llama-server`.
2. **Install** downloads the official llama.cpp source release and builds the Linux x64 CUDA server locally.
3. **Start** launches the saved model configuration.
4. **Stop** terminates the Alicia-managed server process.

Alicia does not silently fall back to another provider when the selected provider is unavailable.

## Models

The **Models** workspace owns provider/model and generation configuration.

- Hugging Face GGUF reference: `owner/model-GGUF[:quant]`.
- Optional context size.
- Optional max output tokens, temperature, top-p, top-k and seed.
- Reasoning enable/disable control.
- Optional positive reasoning token budget when reasoning is enabled.

Blank optional values preserve provider/model defaults. Save the configuration before starting the provider.

## Chat

- `Enter`: send the current draft.
- `Shift+Enter`: insert a newline.
- **Send**: submit the draft.
- **Stop**: cancel the active streamed response after the short startup guard.
- **Retry**: retry the existing unanswered User message after the short interruption guard; Alicia does not duplicate the User message.

The User message is persisted before generation. Partial Assistant output remains transient; only a successfully completed visible Assistant response is persisted.

## Reasoning

When reasoning is enabled and the selected model emits separated reasoning deltas:

- Alicia shows a thinking indicator before the first delta;
- reasoning appears in a darker expandable section, separated from the final answer;
- reasoning is an in-memory presentation snapshot only in the current implementation;
- reloading the conversation or restarting Alicia discards that reasoning snapshot.

## Local data

The desktop composition root stores Alicia data below `.NET`'s `LocalApplicationData/Alicia` directory:

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

The exact platform path is resolved by `Environment.SpecialFolder.LocalApplicationData`.

## CUDA prerequisites

For Alicia-managed llama.cpp installation on Linux x64, the host needs:

- a working NVIDIA driver (`nvidia-smi`);
- CUDA Toolkit (`nvcc`);
- CMake;
- a C and C++ compiler;
- Ninja or Make.

Alicia does not install privileged system packages.

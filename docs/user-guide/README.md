# User guide

Alicia currently provides a local desktop conversation workflow backed by an explicitly selected inference provider.

## Conversations

The **Conversations** workspace owns the conversation history and chat surface.

- Create a conversation with the `+` action.
- Select a conversation from history.
- Search history by title and message content.
- Hover a history item to inspect its recent-message preview.
- Rename or delete a conversation from its `…` menu or context menu.
- Use **Change icon** or **Change color** from either menu to assign a persistent visual identity without opening that conversation.
- Stage 10A provides eight predefined icon categories and seven accent colors; existing conversations use `Conversation + Teal` until customized.
- The history panel can be collapsed; the explicit expanded/collapsed preference is restored on the next launch and applies to wide layouts.
- At 900 DIP or below, history opens as a temporary overlay above the conversation. Closing it, selecting a conversation, or creating a conversation does not overwrite the wide-layout preference.
- Deleting a conversation opens a compact blocking confirmation modal. `Escape` or **Cancel** keeps the conversation; only **Delete** confirms permanent removal.

## Provider

The **Provider** workspace owns provider lifecycle operations.

For `llama.cpp CUDA`:

1. **Detect** checks for a managed or externally installed CUDA-capable `llama-server`.
2. **Install** verifies and builds the llama.cpp release pinned and validated by this Alicia build instead of following a moving `latest` release.
3. **Check update** compares the active Alicia-managed release, Alicia's validated release, and upstream latest release.
4. **Update validated** is enabled only when a stopped Alicia-managed runtime is older than the validated release. The update is always explicit.
3. **Start** launches the saved model configuration.
4. **Stop** terminates the Alicia-managed server process.

Alicia does not silently fall back to another provider when the selected provider is unavailable.

Alicia currently validates managed llama.cpp release `b10435` at source commit `9e40df63ba151d771d8b247ac4011cf203337e99`. If GitHub publishes a newer release, **Check update** may report it as upstream latest, but Alicia will not install it until a later Alicia build validates and pins it. A managed release newer than the current pin is never automatically downgraded. Previous managed releases remain on disk until explicit cleanup.

## Models

The **Models** workspace owns provider/model and generation configuration.

- Hugging Face GGUF reference: `owner/model-GGUF[:quant]`.
- Optional context size.
- Optional max output tokens, temperature, top-p, top-k and seed.
- Reasoning enable/disable control.
- Optional positive reasoning token budget when reasoning is enabled.

Blank optional values preserve provider/model defaults. Save the configuration before starting the provider.

## Conversation setup gate

The Conversation workspace now explains the single next step whenever local AI is not ready. Existing history and messages remain readable while the composer is replaced by one gate action. Depending on the diagnosis, Alicia offers **Configure provider**, **Check provider**, **Install provider**, **Review provider**, **Configure model**, **Review model settings**, **Review model**, **Check provider again**, or **Load model**. Configuration actions navigate to the dedicated Provider or Models workspace; lifecycle actions run through the existing provider commands.

If no conversation exists and local AI is unavailable, the same gate becomes the central onboarding surface and the empty history panel is hidden. Once the provider reaches `Running`, the gate disappears and the normal composer becomes available.

Provider failures are classified before they reach the UI. A model-load failure keeps a still-valid runtime reusable and points back to Models; a network failure offers a new provider check; a missing/unsupported runtime stays a provider setup problem. Alicia does not display raw HTTP response bodies, server log tails, local file paths, or provider exception diagnostics as the user-facing error.

## Chat

- `Enter`: send the current draft.
- `Shift+Enter`: insert a newline.
- **Send**: submit the draft.
- **Stop**: cancel the active streamed response after the short startup guard.
- **Retry**: retry the existing unanswered User message after the short interruption guard; Alicia does not duplicate the User message.

The User message is persisted before generation. Partial Assistant output remains transient; only a successfully completed visible Assistant response is persisted.

## Scroll and streaming

Conversation scrolling has two states:

- **FOLLOWING**: new streamed content stays followed at the bottom when needed; use the centered **↑ Pause auto-scroll** control above the composer to stop following immediately without scrolling;
- **DETACHED**: after pausing explicitly or deliberately scrolling about 32 px above the bottom, streamed chunks never move the viewport.

When detached, the control becomes **↓ Resume & jump to latest**. Scrolling manually back to the bottom does not re-enable following; click **↓ Resume & jump to latest** or send a new User message to do that explicitly. The scroll state and vertical position are stored independently for each conversation and restored after switching conversations or restarting Alicia.

## Reasoning

When reasoning is enabled and the selected model emits separated reasoning deltas:

- Alicia shows a thinking indicator before the first delta; when Reduced Motion is active, that indicator stays static instead of cycling punctuation;
- reasoning appears in a darker expandable section, separated from the final answer;
- reasoning is an in-memory presentation snapshot only in the current implementation;
- reloading the conversation or restarting Alicia discards that reasoning snapshot.


## Accessibility and motion

- The global rail, history actions, composer actions, configuration gate and destructive confirmation expose accessible names/status in addition to color and iconography.
- Keyboard focus is visibly indicated. When the narrow history overlay opens, focus moves to **Search conversations** and Tab/Shift+Tab stay inside until the overlay closes; `Escape` closes it and restores the previous focus when possible.
- The delete confirmation moves focus to **Cancel** and cycles focus between its actions. `Escape`/Cancel restore the previous focus when possible.
- `Escape` dismisses an open delete dialog or narrow-history overlay before it can stop an active streamed response.
- With Reduced Motion enabled, Alicia keeps the thinking label static, makes global-navigation disclosure effectively immediate, and stops indeterminate progress animation while retaining progress/status text.

For deterministic testing, `ALICIA_REDUCED_MOTION=1` forces Reduced Motion and `ALICIA_REDUCED_MOTION=0` forces normal motion.

## Local data

The desktop composition root stores Alicia data below `.NET`'s `LocalApplicationData/Alicia` directory:

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

The exact platform path is resolved by `Environment.SpecialFolder.LocalApplicationData`. `ui-state.json` contains Presentation-only history, per-conversation scroll state, and per-conversation icon/color identity; it is not part of conversation history. Stage 10A writes UI-state version 2 while continuing to read version 1 documents safely.

## CUDA prerequisites

For Alicia-managed llama.cpp installation on Linux x64, the host needs:

- a working NVIDIA driver (`nvidia-smi`);
- CUDA Toolkit (`nvcc`);
- CMake;
- a C and C++ compiler;
- Ninja or Make.

Alicia does not install privileged system packages.


## Retry behavior

Alicia never silently resends a model-generation request after a timeout, disconnect, Stop, or provider error. If an unanswered user message can be tried again, the composer exposes **Retry response**. Choosing it sends a new explicit generation request for that same stored user message; the user message is not duplicated.

Provider maintenance can transparently retry a small number of read-only network checks when the failure is transient. These retries are bounded and stop immediately when the operation is cancelled.

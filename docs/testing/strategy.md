# Testing strategy

The foundation uses xUnit v3 with Microsoft Testing Platform.

Current automated coverage includes:

- architectural dependency boundaries;
- conversation Domain and Application behavior;
- provider-neutral response request/result/chunk contracts, non-streaming completion, and streamed turn orchestration with deterministic responder doubles;
- cancellation, partial-stream failure, stale-history rejection, duplicate-trigger protection, reasoning-only rejection, Content/Reasoning stream separation, and final-visible-content-only Assistant persistence;
- presentation ViewModel behavior for shell default selection/navigation with a retained shared workspace, six-slice `MainViewModel` composition/facade ownership, local conversation, explicit provider selection/configuration, validation/dirty state, provider-default delegation, detect/install/start/stop state, installation-progress projection, incremental Assistant projection, reasoning-step projection, first-delta activity state, Reduced Motion projection, per-conversation `Following`/`Detached` scroll transitions, explicit pause/reattachment controls, short-threshold gesture detachment, narrow-layout history behavior, short Stop/Retry guard windows, Stop, retry, and message workflows;
- versioned JSON Presentation UI-state round trips, malformed-document fallback, invalid conversation-entry filtering, persisted history preference, and per-conversation scroll-state restoration;
- provider-registry explicit routing/no-fallback behavior, versioned configuration round trips and Lot 08 migration, llama.cpp model-reference validation, secure server arguments (loopback, dynamic explicit port, localhost CORS, disabled Web UI, and no command-line API key), ephemeral API-key generation/environment delivery, authenticated `/props` ownership probing that rejects unprotected or foreign local servers, Bearer-authenticated local HTTP calls with no request-body secret leakage, CUDA/server build arguments (including optional context size and provider-owned GPU-layer defaults), Ninja/Make progress parsing, safe release descriptor parsing, CUDA/version probing, OpenAI-compatible SSE parsing for visible and reasoning deltas, optional generation/reasoning override serialization, and HTTP error projection;
- repository tooling self-tests inherited from the proven reference workflow.

A manual smoke test covers the real managed llama.cpp process boundary because source compilation, CUDA availability, Hugging Face download, GPU offload, and loopback server startup depend on the host machine. Future work can add hermetic process fixtures and UI end-to-end coverage without weakening the existing unit/application boundaries.

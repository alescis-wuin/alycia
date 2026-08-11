# Testing strategy

The foundation uses xUnit v3 with Microsoft Testing Platform.

Current automated coverage includes:

- architectural dependency boundaries;
- conversation Domain and Application behavior;
- provider-neutral response request/result/chunk contracts, non-streaming completion, and streamed turn orchestration with deterministic responder doubles;
- cancellation, partial-stream failure, stale-history rejection, duplicate-trigger protection, and final-only Assistant persistence;
- presentation ViewModel behavior for local conversation, provider detect/install/start/stop state, installation-progress projection, incremental Assistant projection, Stop, retry, and message workflows;
- llama.cpp model-reference validation, CUDA/server build arguments (including the required tools tree), Ninja/Make progress parsing, safe release descriptor parsing, server command construction, CUDA/version probing, OpenAI-compatible SSE parsing, and HTTP error projection;
- repository tooling self-tests inherited from the proven reference workflow.

A manual smoke test covers the real managed llama.cpp process boundary because source compilation, CUDA availability, Hugging Face download, GPU offload, and loopback server startup depend on the host machine. Future work can add hermetic process fixtures and UI end-to-end coverage without weakening the existing unit/application boundaries.

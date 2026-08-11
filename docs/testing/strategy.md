# Testing strategy

The foundation uses xUnit v3 with Microsoft Testing Platform.

Current automated coverage includes:

- architectural dependency boundaries;
- conversation Domain and Application behavior;
- provider-neutral response request/result/chunk contracts, non-streaming completion, and streamed turn orchestration with deterministic responder doubles;
- cancellation, partial-stream failure, stale-history rejection, duplicate-trigger protection, and final-only Assistant persistence;
- presentation ViewModel behavior for local conversation, incremental Assistant projection, Stop, retry, and message workflows;
- repository tooling self-tests inherited from the proven reference workflow.

Future provider packages add adapter integration tests behind the Application responder port, then UI end-to-end tests for critical request/response flows.

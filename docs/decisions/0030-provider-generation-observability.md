# ADR 0030 — Provider generation observability without content logging

## Status

Accepted for root Lot 10.7.

## Context

Provider robustness work can now detect, secure, update, and maintain the managed llama.cpp runtime, but Alicia still lacks a structured view of what happened during one generation.

The observability boundary must answer operational questions without turning conversation content into telemetry. The useful fields are provider/model/version identity, end-to-end latency, time to first streamed output, input/output token usage, provider prompt/evaluation timings when available, cancellation, and failure classification.

The Alicia-validated llama.cpp `b10435` source supports OpenAI-compatible streamed chat usage when `stream_options.include_usage=true`. Its final streaming result can also expose the server `timings` object. These fields are metadata and can be consumed without logging prompt or completion text.

## Decision

Application defines an optional provider-neutral `IInferenceProviderObservabilityRuntime` capability and immutable `InferenceProviderGenerationObservation` records.

A generation observation contains only:

- provider identifier and display name;
- selected model reference and runtime version;
- start/completion timestamps;
- `Completed`, `Cancelled`, or `Failed` outcome;
- safe provider failure classification for failed runs;
- end-to-end duration and time to first output;
- input/output/total/cached token counts when supplied;
- prompt-evaluation and generation durations/rates when supplied.

Infrastructure owns provider-specific extraction. The llama.cpp adapter:

- requests streamed `usage` explicitly;
- reads `usage` and `timings` metadata from SSE JSON independently from visible Content/Reasoning deltas;
- records caller cancellation separately from provider failure;
- writes a bounded local JSONL observation log under the Alicia-owned provider `logs/` directory;
- rotates that log at approximately 1 MiB;
- treats observation-log I/O as best-effort so telemetry can never fail generation.

No request messages, prompts, reasoning text, Assistant content, raw response body, API key, endpoint credential, or server log tail is part of the observation record.

Presentation refreshes the latest observation after generation finishes, fails, or is cancelled. The existing **Technical details** disclosure shows the latest safe metrics so root Lot 10.7 does not reintroduce the visual noise reduced by 10.7A.

## Consequences

Observability remains optional and provider-neutral at the Application boundary.

Provider-specific timing fields can be absent without making a generation invalid.

The local observation log is disposable operational data: Lot 10.6 runtime cleanup removes it with the provider `logs/` scope.

Prompt/message content remains available only to the generation path itself and is not duplicated into the structured observability log by default.

Future providers may expose the same Application observation contract even when their native timing/usage metadata differs.

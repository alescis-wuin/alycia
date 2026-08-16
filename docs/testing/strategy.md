# Testing strategy

The foundation uses xUnit v3 with Microsoft Testing Platform.

Current automated coverage includes:

- architectural dependency boundaries;
- conversation Domain and Application behavior;
- provider-neutral response request/result/chunk contracts, non-streaming completion, and streamed turn orchestration with deterministic responder doubles;
- cancellation, partial-stream failure, stale-history rejection, duplicate-trigger protection, reasoning-only rejection, Content/Reasoning stream separation, and final-visible-content-only Assistant persistence;
- presentation ViewModel behavior for shell default selection/navigation with a retained shared workspace and typed gate-driven workspace requests, six-slice `MainViewModel` composition/facade ownership, deterministic Conversation configuration-gate states/actions, central onboarding vs inline gating, local conversation, explicit provider selection/configuration, validation/dirty state, provider-default delegation, detect/install/start/stop state, typed `Missing/Unsupported/Faulted/Network/Model` recovery, safe provider errors, installation-progress projection, incremental Assistant projection, reasoning-step projection, first-delta activity state, Reduced Motion projection, per-conversation `Following`/`Detached` scroll transitions, explicit pause/reattachment controls, short-threshold gesture detachment, narrow-layout history overlay behavior and wide-preference restoration, automatic overlay closure after narrow navigation, delete-modal command state, Escape precedence for temporary surfaces, assistive workspace/history-item status, shell Reduced Motion projection, icon/color history identity editing without forced selection, short Stop/Retry guard windows, Stop, retry, and message workflows;
- versioned JSON Presentation UI-state round trips, v1→v2 backward-compatible reads, malformed-document fallback, independent invalid scroll/identity-entry filtering, persisted history preference, per-conversation scroll-state restoration, and per-conversation visual-identity persistence/removal;
- provider-registry explicit routing/no-fallback behavior, versioned configuration round trips and Lot 08 migration, llama.cpp model-reference validation, secure server arguments (loopback, dynamic explicit port, localhost CORS, disabled Web UI, and no command-line API key), ephemeral API-key generation/environment delivery, authenticated `/props` ownership probing that rejects unprotected or foreign local servers, Bearer-authenticated local HTTP calls with no request-body secret leakage, CUDA/server build arguments (including optional context size and provider-owned GPU-layer defaults), Ninja/Make progress parsing, safe release descriptor parsing, CUDA/version probing, OpenAI-compatible SSE parsing for visible and reasoning deltas, optional generation/reasoning override serialization, finite timeout/caller-cancellation behavior, startup diagnostic classification without user-message leakage, safe HTTP failure classification, bounded transient retry at idempotent GET boundaries, generation-POST single-attempt behavior, validated release-tag/source-commit pinning, managed/update/latest version projection, explicit update enablement, atomic installation-metadata preservation on cancellation, explicit managed-storage inspection/removal boundaries, runtime-only versus runtime-plus-model-cache behavior, retained-release cleanup, path/symlink containment, and raw response-body/log-tail isolation;
- repository tooling self-tests inherited from the proven reference workflow.

A manual smoke test covers the real managed llama.cpp process boundary and UI focus/motion behavior because source compilation, CUDA availability, Hugging Face download, GPU offload, loopback server startup, platform focus traversal and OS motion preferences depend on the host machine. Future work can add hermetic process fixtures and UI end-to-end coverage without weakening the existing unit/application boundaries.


### Lot 10.4 retry invariants

Tests must prove that generation POSTs are attempted exactly once on transient server failure, explicit Conversation Retry reuses the unanswered persisted user message, cancellation never triggers a hidden replay, transient idempotent GETs are retried only within their fixed attempt budget, permanent HTTP responses are not retried, and cancellation interrupts retry backoff.


### Lot 10.5 validated update invariants

Tests must prove that the validated llama.cpp tag and source commit are pinned, the official release lookup must still resolve that tag to the expected commit, download targets the immutable commit tarball, update checking distinguishes managed installed/validated/upstream-latest releases, an older managed release is the only normal update candidate, a newer managed release is never automatically downgraded, update execution requires an explicit command, safe update failures leave a healthy runtime usable, and cancellation before metadata promotion preserves the previously active installation descriptor.


### Lot 10.6 managed storage maintenance invariants

Tests must prove that storage inspection keeps runtime and model-cache sizes separate; retained-release cleanup removes only recognized inactive managed releases while preserving the active release, unknown directories and model cache; runtime-only uninstall removes runtime metadata/releases/staging/logs but preserves model cache, provider configuration, conversations and legacy settings; runtime-plus-cache removal is a distinct explicit scope; cancellation before mutation changes nothing; invalid metadata paths cannot make cleanup escape the managed root; symbolic links/reparse points are never followed; Presentation request/Cancel paths perform no deletion; confirmation selects exactly one removal mode; and destructive actions remain unavailable while the provider runs.

### UI refinement 10.7A visual-contract invariants

The Provider/Models cleanup is Presentation-only. Validation must prove that the final Provider and Models XAML preserve the exact baseline binding/command names, that no Application/Infrastructure/ViewModel source changes are part of the lot, and that XAML remains well formed. Manual smoke must verify that lifecycle/update/maintenance enabled states and confirmations behave exactly as before, Maintenance and technical detail disclosure remain keyboard-operable, visible focus remains clear, and narrow windows do not introduce horizontal scrolling or clipped primary controls.

# ADR 0027 — Validated llama.cpp release updates

## Status

Accepted for root Lot 10.5.

## Context

The managed llama.cpp installer previously resolved GitHub `latest` during installation. That made a fresh installation depend on a moving upstream release and provided no explicit distinction between the active managed release, the version Alicia has validated, and a newer upstream release. It also made an update action difficult to reason about safely.

## Decision

- Managed installation and update target a release explicitly pinned by the Alicia build, currently `b10435` at source commit `9e40df63ba151d771d8b247ac4011cf203337e99`.
- Alicia verifies the official GitHub release tag still resolves to that commit, then downloads the source tarball by immutable commit SHA rather than by a moving `latest` alias.
- `IInferenceProviderUpdateRuntime` is an optional provider-neutral capability separate from the base lifecycle contract. It exposes the validated version, explicit update checking, and explicit update execution.
- Update checking reports the managed installed release, Alicia's validated release, and upstream latest release. A newer upstream release is informational until a later Alicia build validates and pins it.
- Alicia offers an update only when a managed installed release is older than the validated release. It never automatically downgrades a managed runtime that is newer than the current validated pin.
- Update remains an explicit user action and is unavailable while the local model is running.
- A candidate release is built and CUDA-validated in its version-specific release directory before `installation.json` is atomically switched. Failure or cancellation before that switch leaves the previous active metadata intact.
- Previous managed release directories are retained. Cleanup and destructive removal remain Lot 10.6 responsibilities.

## Consequences

Fresh managed installs are reproducible against a reviewed release rather than whatever GitHub marks latest at runtime. Upstream freshness is still visible without becoming an implicit trust or execution decision. The version-specific release layout provides a rollback-safe preservation boundary while keeping destructive cache/runtime cleanup out of the update path.

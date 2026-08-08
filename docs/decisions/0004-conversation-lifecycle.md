# ADR 0004: Conversation lifecycle metadata and operations

## Status

Accepted.

## Context

The conversation core can create conversations, append messages, and persist a
single aggregate by identifier. A usable conversation navigator also needs
stable metadata and explicit lifecycle operations without coupling presentation
code to storage details.

## Decision

`Conversation` owns a normalized title and a monotonic `UpdatedAt` timestamp.
Message appends advance activity time, while explicit renames update both title
and activity time.

The application repository port exposes:

- lookup by identifier;
- summary listing;
- save;
- deletion.

Application use cases wrap loading, listing, renaming, and deletion so
presentation code can depend on application behavior rather than infrastructure
implementations.

The JSON repository stores schema version 2 documents, lists summaries by recent
activity, ignores unrelated JSON filenames, validates filename/document identity,
and keeps read compatibility with the unversioned documents emitted by the
previous local-persistence slice.

## Consequences

- a future sidebar can list and order conversations without referencing
  infrastructure;
- titles can be renamed independently of message content;
- old local conversation files remain readable;
- listing currently reads each conversation document and is intentionally
  unindexed; a dedicated metadata index can be introduced later if scale
  requires it;
- deletion is permanent at the repository boundary; archive/trash behavior can
  be layered above this port later.

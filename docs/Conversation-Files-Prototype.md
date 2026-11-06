# Conversation files prototype

Branch: `prototype/conversation-files`, derived from `prototype/local-ollama-chat`
at `3e1c49f`. No merge into `develop` or `main` is needed.

## Start on Linux

Close the previous demo API before starting this branch (both use port 5187).
Keep the existing `.env` and PostgreSQL volume.

```bash
git fetch origin
git switch prototype/conversation-files
make demo-api
```

In another terminal, with Ollama already running and a local model installed:

```bash
make demo-client
```

The launcher restores tools/packages, checks PostgreSQL password authentication,
applies the existing migrations plus `ConversationAttachments`, then builds and
runs the API container. Existing conversations and messages are preserved. The
first launch downloads the .NET image and the PdfPig NuGet package.

The API now runs in the `chat-api` Compose service, with Linux host networking,
listening only on `127.0.0.1:5187`. It reaches PostgreSQL on the published loopback
port (5433 by default) and Ollama at `127.0.0.1:11434`. This preserves the existing
Development-only, explicit opt-in and loopback/Origin guards. The regular `api`
service and its port 8080 are separate from this demonstration.

Docker Desktop requires host networking support to be explicitly enabled;
that environment has not been tested. Linux Docker Engine is the target.

Stop the API with Ctrl+C. For an explicit stop from another terminal:

```bash
docker compose -f compose.yaml -f compose.demo.yaml --profile files stop chat-api
```

Do not delete volumes to fix startup or migration errors. If PostgreSQL rejects
the password, match `.env` to the existing database password.

## Import and use files

Create or select a conversation. Use **+ Fichiers** for files/archives, **Dossier**
for directories, or drop files/directories onto the window. Directory imports
preserve relative names, including the selected root folder. Empty folders are
ignored; symbolic links are rejected rather than followed.

Each imported file appears in the message timeline with its extraction status,
size, **Télécharger** and **Retirer** actions. The original bytes are downloadable.
An archive and its extracted members have separate rows. Removing an archive
also removes its extracted members. Removing an individual member leaves the
original archive unchanged. Deleting a conversation removes all its metadata,
messages and stored files.

Ask a question after importing. Text extraction happens in the backend; all model
requests still pass through the backend to local Ollama. Files are never executed.

| Input | Context extraction |
| --- | --- |
| Text, source code, Markdown, JSON, CSV, logs, unknown extensions containing text | UTF-8 or BOM-marked UTF-16 |
| PDF | Embedded text, up to 200 pages; no OCR |
| DOCX, ODT, PPTX | Main document/slide XML text; formatting is discarded |
| XLSX | Sheet cell references and cached values; shared strings resolved; formulas are not evaluated |
| ZIP, TAR, TAR.GZ, TGZ, GZ | Original retained, members imported on one level |
| Nested archives, RAR, 7z, legacy binary office documents and other binary formats | Original retained; no extraction |
| Images, including SVG | Original retained; no image analysis or OCR |

An accepted upload does not imply that its format can be read by the model. The
row status reports whether text was extracted. Corrupt/encrypted supported
archives reject the whole batch. Documents whose text cannot be extracted remain
downloadable. Layout, charts, scanned PDFs and visual content are outside scope.

## Sliding context

An import is anchored to the next user-message sequence, without creating a fake
user/assistant turn. Its text remains eligible while that exchange is inside the
sliding history window. It is removed from prompts when the exchange leaves the
window, but remains in storage and in the visible history.

The backend reserves at most half the remaining input budget, capped at 4096
conservative token-estimate units, for file excerpts. Newer files have priority;
a large file can consume that budget. The usual system prompt, output reserve,
whole recent turns and UTF-8 byte estimate remain in effect. Excerpts are explicitly
labelled as untrusted document data in the final user prompt. This is bounded
context attachment, not semantic search, embeddings or RAG. The full contents of
a directory are not guaranteed to fit in one request.

`Chat__MaxHistoryTurns`, `Chat__ContextTokens`, `Chat__MaxOutputTokens` and
`Chat__TimeoutSeconds` are forwarded to the container. For a visible window demo:

```bash
Chat__MaxHistoryTurns=1 make demo-api
```

Import a short note and ask about it. It is included in that request and the next
request while the anchored turn is retained; after another exchange it expires.
An assistant response can still contain information quoted earlier from a file.
Removing a file does not rewrite existing chat messages.

## Persistence and limits

- Docker volume `chat_files` (Compose project prefix added automatically), mounted
  at `/app/files`, stores originals and extracted archive members by generated IDs.
- PostgreSQL `chat.attachments` stores conversation ID, import batch ID, optional
  archive parent ID, original relative name, size, SHA-256, extraction status,
  extracted text, timeline sequence and creation time. Conversation deletion
  cascades to attachment rows.
- `ALYCIA_FILES_DIRECTORY` controls the backend storage root outside Compose; its
  default is `data/files` under the content root. Host storage is not automatically
  migrated to the Docker volume.
- Limits: 25 MiB per file/member; 100 MiB per batch counting originals and expanded
  members; 100 entries per batch including archive originals; 500 entries and
  1 GiB per conversation; 16 directory levels in the desktop importer.
- Extracted text is capped at 200,000 UTF-8 bytes per file. Office extraction also
  caps XML entry sizes, total expanded XML and entry count. XML external entities
  are disabled. Absolute paths, traversal and archive links are rejected.
- A batch UUID makes identical API upload retries idempotent. The desktop picker
  creates a new batch for each import action: after an ambiguous network error,
  refresh before importing again to avoid duplicates.

Binary writes complete before the metadata transaction commits. Concurrency uses
the existing conversation revision. Filesystem and PostgreSQL cannot share one
atomic transaction: a crash or ambiguous database failure can leave unreferenced
bytes. Failed deletion cleanup is logged; there is no automatic orphan collector
in this prototype. Backups must capture both PostgreSQL and `chat_files` together.
A missing file volume produces a download error, rather than an empty file.

This remains a single-user local prototype without authentication, antivirus,
per-user quotas or an isolated document-processing worker. Parsing is synchronous
within the import request and bounded by the limits above.

## Verification

Implemented checks:

- Solution compilation: zero warnings/errors; Release backend publish.
- 58 xUnit tests passing, including PostgreSQL-backed migration/import/restart,
  archive traversal and size rejection, context budgets, document extraction,
  multipart client requests and attachment rendering with Avalonia headless/Skia.
- Actual HTTP API with PostgreSQL 17 and a simulated Ollama endpoint: uploads,
  original downloads, conversation scoping, duplicate-batch handling, Origin guard,
  ZIP/TAR/TAR.GZ/GZip, context inclusion/expiry, API restart persistence and deletion.
  The idempotent migration script was applied twice successfully.
- C# whitespace checks, `git diff --check`, shell syntax and Compose configuration.

The Docker daemon, real Ollama inference and native desktop file-picker/drop
integration are not available in the verification environment. Container runtime,
volume ownership on the target host and native drag-and-drop remain manual checks.

On your machine, verify: import a short text and a ZIP, ask about their content,
download the original, restart with `make demo-api`, reopen the conversation,
then remove a disposable attachment and conversation. Check folder picker and
drag-and-drop separately on your desktop session.

For the existing automated database tests, set `ALYCIA_CHAT_TEST_CONNECTION` to
a disposable local PostgreSQL instance whose user can create/drop test databases,
then run `make test`. Without it, PostgreSQL tests are explicitly skipped.

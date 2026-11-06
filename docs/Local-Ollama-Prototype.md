# Local Ollama chat prototype

Branch: `prototype/local-ollama-chat`.
Base: `develop` at `8179f9d` (Sprint 6), not the older `main` foundation.

This isolated demo adds a simple, single-user chat application. It does not claim
completion of Sprint 7 or replace its owner-initialization/authentication design.

## Start the demonstration

Prerequisites: .NET SDK 10, Docker Engine + Compose with `--wait` support,
Python 3, Bash, GNU Make, Ollama, and a graphical desktop for Avalonia.
Initial package/image/model downloads require network access.

From a checkout with no uncommitted work:

```bash
git fetch origin
git switch prototype/local-ollama-chat
```

1. Start Ollama if it is not already running (leave its terminal open):

   ```bash
   ollama serve
   ```

2. In another terminal, check installed models. Use an existing local text model
   or download this small example before the demonstration:

   ```bash
   ollama list
   ollama pull qwen2.5:1.5b
   # Warm up the model before opening the client; leave with /bye.
   ollama run qwen2.5:1.5b
   ```

   The example tag is listed in the [official Ollama catalog](https://ollama.com/library/qwen2.5:1.5b).
   Hardware performance is not established by this prototype's automated tests.

3. From the repository root, start PostgreSQL, migrate, then run the host API:

   ```bash
   make demo-api
   ```

   The script creates `.env` with a random database password only when absent.
   It never replaces an existing file. If `.env` still has the example placeholder,
   supply your actual local PostgreSQL password first. Changing that variable does
   not reset an already-initialized PostgreSQL volume's password.

   `compose.demo.yaml` publishes PostgreSQL only on `127.0.0.1:5433` and preserves
   the existing named volume. The API runs on the host at `127.0.0.1:5187`, so it
   can reach the host's Ollama without Docker host/network tricks. It uses
   `Development` and explicit chat opt-in. The normal Compose API is not used.

4. In a separate terminal at the repository root:

   ```bash
   make demo-client
   ```

5. Select the model, optionally change the instruction under **Instruction système**,
   enter a title, then click **Nouvelle conversation**. Send with **Envoyer** or
   Ctrl+Enter. Enter alone inserts a newline. Replies appear when complete.

If port 5433 is occupied, launch the API with
`ALYCIA_DEMO_POSTGRES_PORT=5434 make demo-api`. If 5187 is occupied, stop the
conflicting process or run the API/client manually with matching loopback URLs.
Use `curl http://127.0.0.1:5187/healthz` to check the API; this technical check
does not itself check PostgreSQL or Ollama. **Actualiser** checks the chat services.

Stop the API and client with Ctrl+C/close. PostgreSQL remains running; stop it
without deleting data using `docker compose stop postgres`. Do not delete volumes
to resolve a connection or migration problem.

## Five-minute demo checklist

- Create **Démo locale** and send “Mon projet s’appelle Orion. Réponds brièvement.”
- Ask “Comment s’appelle mon projet ?” to demonstrate recent context.
- Rename the conversation with the title field and **Renommer**.
- Create a second conversation; switch between them to show separate histories.
- Restart the client and API, then reopen the first conversation: messages persist.
- Stop Ollama, try sending, and show the error with the draft retained. Restart
  Ollama and retry; a confirmed prior request is replayed without a duplicate.
- Delete a disposable conversation and confirm the dialog.

For a clear sliding-window demo, start the API with
`Chat__MaxHistoryTurns=1 make demo-api`: only the latest complete exchange is
included with each new question. Earlier messages remain visible and stored.

## Architecture and repository analysis

`main` contained the initial API/Compose foundation only. `develop` already had
Avalonia 12.1.4, .NET 10, EF Core/Npgsql, a Workspaces module separated into
Domain/Application/Infrastructure, explicit migrations and focused xUnit tests.
It had no chat entities, Ollama connector or message context management.

This branch follows that module separation:

- `Alycia.Modules.Chat.Domain`: conversations, validated titles, immutable
  messages, timestamps, sequence numbers and conversation revisions.
- `Alycia.Modules.Chat.Application`: orchestration, repository/model interfaces,
  complete-turn sliding window, input budgets, idempotent sends.
- `Alycia.Modules.Chat.Infrastructure`: EF Core/PostgreSQL and the HTTP Ollama adapter.
- `Alycia.Api/Chat`: Development-only routes, local peer checks and error mapping.
- Avalonia: HTTP client and chat window; no direct access to Ollama or PostgreSQL.
  The earlier diagnostics/Workspace window is preserved behind its button.

The new PostgreSQL `chat` schema has `conversations`, `messages`, and its own
`__EFMigrationsHistory`. `make migrate` now applies Workspaces then Chat explicitly.
There is no schema creation or migration on API startup. Module migrations are
not one transaction spanning both modules.

Successful turns save the user message, assistant answer and conversation
revision in one transaction. A failed or cancelled generation leaves no partial
turn; the unsent draft remains in the open client. A conversation revision check
prevents concurrent sends/rename/delete from silently corrupting message order.
The losing generation returns 409 and can be retried after refresh.

Each send has a request UUID. Replaying that UUID and the same text returns the
stored pair; using different text with it returns 409. A request UUID is retained
for a failed retry in the open client, not across client restarts. If a connection
is lost at commit time, refresh before resending to determine what was saved.

## Context and configuration

The context always includes the conversation's system instruction (if nonempty),
the newest complete user/assistant exchanges that fit, and the current user input.
It evicts the oldest exchanges first; it never sends an orphan assistant answer.
It does not summarize, rewrite or delete persisted history. UI history loads
100 messages per page, with **Charger les messages précédents** for older pages.

| Environment variable | Default | Purpose |
| --- | --- | --- |
| `ALYCIA_ENABLE_LOCAL_CHAT` | `false` | Opt-in, effective only in Development |
| `ALYCIA_CHAT_CONNECTION` | Required | PostgreSQL connection string; set by launcher |
| `ALYCIA_OLLAMA_BASE_URL` | `http://127.0.0.1:11434/` | Loopback HTTP(S) origin only |
| `ALYCIA_API_BASE_URL` | `http://127.0.0.1:8080` | Client endpoint; demo-client sets port 5187 |
| `Chat__MaxHistoryTurns` | `12` | At most 24 prior messages, in complete pairs |
| `Chat__ContextTokens` | `8192` | Sent to Ollama as `num_ctx` |
| `Chat__MaxOutputTokens` | `1024` | Reserved output budget and `num_predict` |
| `Chat__SafetyTokens` | `256` | Additional chat-template margin |
| `Chat__TimeoutSeconds` | `180` | Maximum inference time; range 1–600 seconds |

Input cost uses UTF-8 bytes plus per-message overhead as a conservative estimate,
not the model's tokenizer. It can discard more history than necessary. Set
`ContextTokens` to a value supported by the selected model; exact token usage and
model-specific template overhead are not guaranteed. Oversized current input is
rejected rather than silently truncated. The UI shows how many prior messages
were included, not an exact tokenizer count.

Model and system instruction are fixed per conversation for this prototype;
create another conversation to change them. Local model names come from Ollama
`GET /api/tags`. Known cloud tags/remote aliases are filtered; no model is pulled
automatically. The adapter uses [Ollama chat](https://docs.ollama.com/api/chat)
with `stream: false` and the configured context/output budgets.

## API surface

All routes have prefix `/api/chat`. Success bodies are typed JSON resources;
failures use the existing envelope fields (`success`, `status`, `code`, `message`,
`data`, `errors`, `traceId`, `timestamp`). Delete succeeds with 204 and no body.
Framework-level malformed JSON/unsupported content type can use standard ASP.NET
400/415 responses instead of the envelope.

| Method and path | Action |
| --- | --- |
| `GET /models` | List local Ollama models |
| `GET /conversations` | List conversations, most recently updated first |
| `POST /conversations` | Create with `title`, `model`, optional `systemPrompt` |
| `PATCH /conversations/{id}` | Rename with `title` |
| `DELETE /conversations/{id}` | Delete conversation and messages |
| `GET /conversations/{id}/messages?before=N` | Read a page before a sequence number |
| `POST /conversations/{id}/messages` | Send `requestId` (UUID) and `content` |

## Verification performed

Verified on .NET SDK 10.0.403, Avalonia 12.1.4 and PostgreSQL 17.11:

- Solution compilation; the final changed client/test build has zero warnings/errors.
- 49 unique xUnit cases passed, including the existing suites. The PostgreSQL test
  was enabled and executed, not skipped. The headless UI font setup was corrected
  to register the same Inter font as production, then that test was rerun successfully.
- Real PostgreSQL: migrations twice, persisted history across new contexts,
  duplicate-request replay, rollback on model error/concurrent update, pagination,
  rename persistence and cascade deletion.
- Actual Kestrel HTTP requests with real PostgreSQL and a simulated Ollama HTTP
  server: create/send/rename/restart/history, window truncation, replay/conflict,
  oversized input, dependency failure, concurrent rename during generation,
  deletion, Origin rejection and disabled/Production 404 behavior.
- Headless Avalonia/Skia: create, rename, send, failed draft retention and confirmed
  deletion. A rendered frame was visually inspected.

To run the repository checks:

```bash
make check
# Optional database integration (use a local TEST account with CREATEDB):
ALYCIA_CHAT_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=postgres;Username=TEST_USER;Password=TEST_PASSWORD' \
  dotnet test tests/Alycia.Modules.Chat.Tests
```

The database test creates and drops a uniquely named test database. It does not
reset the application database. Without the variable it is explicitly skipped.

The execution sandbox blocks the named pipes used by the normal solution-based
formatter, so `make check` could not be completed as one command here. C# whitespace
was formatted/verified using `dotnet format whitespace --folder` on changed files;
tests and compilation were executed separately. Shell syntax and Git whitespace
checks were also performed.

## Remaining limits

- Real Ollama model inference, latency, GPU/RAM behavior and native desktop
  interaction on the demo machine still need the short checklist above. Automated
  tests use simulated model responses, not evidence of real model quality.
- Docker is unavailable in the test environment. The launcher/Compose configuration
  needs an initial run on the demo machine; direct PostgreSQL and API paths were tested.
- Single local user only: no Identity, owner initialization, account isolation,
  Workspace association, encryption-at-rest, backup UI or production deployment.
  Chat routes are absent in Production; active routes reject non-loopback peers
  and browser Origin headers. Do not use this as a remote authentication scheme.
- Text-only replies in one block; no streaming, Markdown rendering, tools,
  attachments, RAG, editing/regeneration of messages or automatic summaries.
- Cancellation is best effort if a commit already completed. Unsent drafts are
  not persisted after closing the client. The conversation list is not paginated.
- No changes to the authentication branch, no merge into `develop` or `main`.

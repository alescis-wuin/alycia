
# Alycia

Alycia is a multi-backend generative AI client with a document knowledge hub and retrieval-augmented generation (RAG).

## Local Ollama demo — this prototype branch

**Windows:** [one-command setup, PowerShell launchers and Docker Desktop](docs/Windows-Prototype.md).
The Windows launcher installs/checks dependencies, initializes persistent services
and opens the native Avalonia client; it does not require Bash, Python or Make.

This derived branch, `prototype/conversation-files`, adds file/folder/archive
imports, attachment actions, a persistent Docker file volume and bounded file
context. **Start here: [conversation files setup and limits](docs/Conversation-Files-Prototype.md).**
`make demo-api` now runs the chat API in Docker with Linux host networking.

This branch adds a working chat flow through **Avalonia → ASP.NET Core → local
Ollama**, with conversations and messages stored in PostgreSQL. It starts from
`develop` and is separate from the Sprint 7 authentication work.

After installing a local model and starting Ollama, run `make demo-api` and then
`make demo-client` in separate terminals. The launcher requires .NET 10, Docker
Compose, Python 3 and Bash; it preserves existing database volumes and `.env`.

Original chat architecture: [setup, demo checklist and verification limits](docs/Local-Ollama-Prototype.md).
The remaining sprint sections describe the earlier foundation. On this branch,
chat is the startup window; backend/Workspace diagnostics remain available from it.

## Current scope

Sprint 1 established the .NET 10 API and infrastructure foundation.

Sprint 2 established GitHub repository governance, protected branches, signed commits, and sprint traceability.

Sprint 3 introduces the first Avalonia desktop client for Linux, displaying the availability of the existing API through its technical `/healthz` endpoint.

The solution currently contains:

- An ASP.NET Core 10 API.
- PostgreSQL infrastructure managed through Docker Compose.
- Structured logging with Serilog.
- A technical API health endpoint.
- An Avalonia desktop client for backend status monitoring.

Sprint 6 adds a Development-only, opt-in, local-machine Workspace create/read vertical slice using EF Core and PostgreSQL. No business endpoint is available in Production, and no full MVP capability or authentication is complete.

## Sprint 6 Workspace development slice

Sprint 6 provides persistent Workspace creation and retrieval through Avalonia,
ASP.NET Core, Workspaces Application/Domain, a dedicated EF Core repository,
and PostgreSQL. This is not a completed MVP capability.

See [Sprint 6 Workspaces development and migration guide](docs/Alycia-Sprint-6-Workspaces.md)
for the temporary local-only security exception, opt-in configuration, explicit
`make migrate` workflow, UI instructions, failures, and limitations.

## Prerequisites

- .NET SDK 10
- Docker Engine and Docker Compose
- GNU Make
- Git
- Bash or fish
- A graphical Linux desktop environment for the Avalonia client
- Network access for initial NuGet restores and Docker image downloads

## Local configuration

Create a local environment file from the example:

    cp .env.example .env

Replace the placeholder PostgreSQL password with a strong, randomly generated secret before starting the services.

The local `.env` file is excluded from Git and must never be committed.

If `.env` already exists with a securely generated password, keep the existing file.

## Local development quality checks

Alycia provides local formatting, validation and test commands
through GNU Make. These commands do not require CI/CD.

### Formatting

On a fresh checkout, restore .NET dependencies first:

    dotnet restore Alycia.slnx

Run formatting before staging changes:

    make format

This command formats relevant modified C# files using
`dotnet format` and removes trailing whitespace from supported
modified text files.

It does not stage files or modify the Git index.

Review the resulting changes before staging:

    git diff
    git status --short

### Linting

Check formatting without modifying files:

    make lint

The command reports formatting and whitespace violations
and returns a non-zero exit code when checks fail.

### Unit tests

Run the focused xUnit test suite:

    make test

The xUnit suite covers the health probe, Workspace services, chat context and
Ollama contracts, HTTP client behavior, and the Avalonia chat flow on the headless
platform. PostgreSQL integration is enabled by `ALYCIA_CHAT_TEST_CONNECTION`;
without that variable the database test is explicitly skipped. See the prototype
guide for setup and remaining real-model/native-desktop checks.

### Combined checks

Run linting followed by the unit tests:

    make check

This command stops if linting fails. It does not install
Git hooks or run CI/CD.

### Optional Git hooks

Install the repository-local pre-commit hook explicitly:

    make install-hooks

Installation is optional, idempotent and does not modify
global Git configuration.

Existing hook configurations are preserved. Installation
fails with a diagnostic if a conflict is detected.

The pre-commit hook checks staged whitespace using:

    git diff --cached --check

It rejects invalid staged changes without modifying files
or the Git index.

### Recommended Git workflow

1. Make the required source changes.
2. Run `make format`.
3. Review the working-tree diff.
4. Run `make check`.
5. Stage the reviewed files using `git add`.
6. Inspect staged changes using `git diff --cached --check`
   and `git diff --cached`.
7. Create a signed commit using `git commit -S`.

If the hook rejects a commit, correct the working files,
review the changes and stage the corrected files again.
The hook never stages changes automatically.

### Logging inspection

Start the local services when required:

    make up

Inspect API console logs:

    docker compose logs --tail=50 api

Inspect structured JSON Lines logs:

    docker compose exec -T api sh -c \
      'tail -n 10 /app/logs/alycia-*.jsonl'

HTTP request events include timestamps, status codes,
elapsed durations and trace correlation identifiers.

Do not include secrets or sensitive request data in
diagnostic reports.

## Build

From the repository root:

    make build

This command builds `Alycia.slnx` using the installed .NET SDK.

The solution includes both the API and the Avalonia desktop client.

Alternatively:

    dotnet build Alycia.slnx

## Start services

    make up

This builds the API Docker image and starts the API and PostgreSQL containers.

The initial startup may require downloading Docker images and NuGet packages.

Check the service status:

    make status

## API health

The API is published on the host loopback interface only.

Check the API:

    curl -i http://127.0.0.1:8080/healthz

Expected healthy response:

- HTTP status: `200 OK`
- Content type: `text/plain`
- Response body: `Healthy`

The endpoint is technical, unauthenticated, and does not expose business data.

It checks API health only. It does not verify PostgreSQL availability.

The endpoint is separate from the JSON response envelope intended for application endpoints.

For development-only OpenAPI/Scalar, request trace inspection,
and targeted JSON Lines exports, see
[Local API contracts and diagnostics](docs/Alycia-Developer-Diagnostics.md).

## Avalonia desktop client (Linux)

The Avalonia client provides a minimal graphical interface for checking API availability.

It requires .NET SDK 10 and a graphical Linux desktop environment.

### Build and launch

Build the client:

    dotnet build src/Alycia.Client.Avalonia/Alycia.Client.Avalonia.csproj

Launch it:

    dotnet run --project src/Alycia.Client.Avalonia/Alycia.Client.Avalonia.csproj

The client opens independently of API availability.

### API configuration

The default API base URL is:

    http://127.0.0.1:8080

Override it without recompilation using the `ALYCIA_API_BASE_URL` environment variable:

    env ALYCIA_API_BASE_URL=http://127.0.0.1:8080 dotnet run --project src/Alycia.Client.Avalonia/Alycia.Client.Avalonia.csproj

The base URL must be an absolute HTTP or HTTPS address.

Plain HTTP is accepted only for loopback addresses. Non-local connections require HTTPS with normal certificate validation.

Malformed addresses are rejected without terminating the client.

Remote API connectivity has not been validated in Sprint 3.

### Backend status

The client sends an asynchronous `GET /healthz` request to the configured API.

A request timeout of five seconds prevents indefinite waiting.

The interface displays three main states:

- `Checking...`: a health request is in progress.
- `Available`: the API returned HTTP 200 with the exact plain-text body `Healthy`.
- `Unavailable`: the health check failed or returned an unexpected result.

Unavailable states include:

- Invalid API base URL.
- Connection refusal or network failure.
- HTTP errors, including HTTP 503.
- Unexpected response bodies.
- Request timeout.
- Unexpected errors during the health check.

The interface remains responsive during requests.

The `Check again` button allows manual rechecking without restarting the client. It is disabled while a check is in progress to prevent overlapping requests.

Automatic periodic polling is not implemented.

### Manual status verification

With the Avalonia window open, perform the following checks.

**1. Available backend**

Start the API:

    make up

Confirm its response:

    curl -i http://127.0.0.1:8080/healthz

Expected result:

- HTTP 200 with `Healthy`.
- Avalonia displays `Available`.

**2. Unavailable backend**

Stop only the API container:

    docker compose stop api

Click `Check again` in Avalonia.

Expected result:

- Avalonia displays `Unavailable`.
- The client remains open and responsive.
- The button remains available for subsequent checks.

**3. Backend recovery**

Restart the API:

    docker compose start api

Confirm that the endpoint is healthy:

    curl -i http://127.0.0.1:8080/healthz

Click `Check again` without restarting Avalonia.

Expected result:

- Avalonia returns to `Available`.

**4. Invalid API configuration**

Close the Avalonia window and launch it with an invalid address:

    env ALYCIA_API_BASE_URL=invalid-url dotnet run --project src/Alycia.Client.Avalonia/Alycia.Client.Avalonia.csproj

Expected result:

- Avalonia displays `Unavailable`.
- The message is `Invalid API base URL.`
- The client remains functional.

### Health-check limitations

The Avalonia status client verifies only the technical `/healthz` response.

An `Available` result does not guarantee:

- PostgreSQL availability.
- Authentication or session availability.
- Business endpoint availability.
- AI provider connectivity.
- Document indexing or RAG availability.

These capabilities are outside the scope of Sprint 3.

## PostgreSQL health

Check PostgreSQL readiness inside its container:

    docker compose exec postgres pg_isready -U alycia -d alycia

Expected result: the server accepts connections.

PostgreSQL has no published host port.

## Logs

Display API container logs:

    docker compose logs --tail=50 api

Display PostgreSQL container logs:

    docker compose logs --tail=50 postgres

The API writes structured JSON Lines logs to `/app/logs` inside its container.

A named Docker volume preserves these files when containers are stopped and recreated.

Default log settings:

- Daily rolling files
- Maximum file size: 10 MiB
- Maximum retained files: 14

The following environment variables configure file logging:

- `ALYCIA_LOG_PATH`
- `ALYCIA_LOG_FILE_SIZE_BYTES`
- `ALYCIA_LOG_RETAINED_FILES`

HTTP request events include the request method, path, response status, elapsed duration, and trace identifier.

Request bodies, authorization headers, and secrets must not be logged.

## Run the API outside Docker

The API can also run directly under .NET:

    dotnet run --project src/Alycia.Api --urls http://127.0.0.1:8080

Run this command only when port 8080 is not already occupied by the Docker API.

The API does not require a direct PostgreSQL connection for its current technical health endpoint.

Local file logs are written relative to the process working directory unless `ALYCIA_LOG_PATH` is configured.

## Stop services

    make down

This stops and removes the Compose containers and network without deleting named volumes.

Do not use `docker compose down --volumes` when persistent data must be retained.

## Known limitations

- The technical health endpoint does not check PostgreSQL.
- Sprint 6 includes the Workspaces EF Core migration, applied explicitly with `make migrate`.
- Workspace create/read endpoints exist only under the Sprint 6 Development/opt-in/loopback exception; no authentication is implemented.
- The Avalonia Linux client provides backend status and the temporary Development-only Workspace create/read controls.
- MAUI and Windows client validation remain deferred.
- Functional parity between Avalonia and MAUI has not been established.
- Remote HTTPS connectivity has not been validated.
- API access through Docker Compose is restricted to the host loopback interface.
- Full backup and restore procedures are not implemented.
- No CI/CD pipeline is configured.

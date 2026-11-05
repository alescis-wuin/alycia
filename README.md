
# Alycia

Alycia is a multi-backend generative AI client with a document knowledge hub and retrieval-augmented generation (RAG).

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

No business capabilities, authentication, or EF Core migrations are implemented.

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
- There is no application database schema or EF Core migration.
- No business endpoints or authentication are implemented.
- The Avalonia Linux client currently provides backend status only.
- MAUI and Windows client validation remain deferred.
- Functional parity between Avalonia and MAUI has not been established.
- Remote HTTPS connectivity has not been validated.
- API access through Docker Compose is restricted to the host loopback interface.
- Full backup and restore procedures are not implemented.
- No CI/CD pipeline is configured.

# Alycia

Alycia is a multi-backend generative AI client with a document knowledge hub and retrieval-augmented generation (RAG).

## Current scope

Sprint 1 establishes the technical foundation.

The solution currently contains one ASP.NET Core 10 project, PostgreSQL infrastructure, Docker Compose configuration, and structured logging.

No business features, authentication, graphical clients, or database migrations are implemented at this stage.

## Prerequisites

- .NET SDK 10
- Docker Engine and Docker Compose
- GNU Make
- Git
- Bash
- Network access for the initial NuGet restore and Docker image downloads

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

- HTTP status: 200
- Response body: Healthy

The endpoint is technical, unauthenticated, and does not expose business data.

It checks API health only. It does not verify PostgreSQL availability.

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

The API does not require a direct PostgreSQL connection in Sprint 1.

Local file logs are written relative to the process working directory unless `ALYCIA_LOG_PATH` is configured.

## Stop services

    make down

This stops and removes the Compose containers and network without deleting named volumes.

Do not use `docker compose down --volumes` when persistent data must be retained.

## Known limitations

- The technical health endpoint does not check PostgreSQL.
- There is no application database schema or EF Core migration.
- No business endpoints or authentication are implemented.
- API access is restricted to the local host during this foundation sprint.
- Remote HTTPS access, graphical clients, and business features are not yet available.
- Full backup and restore procedures are not implemented.
- No CI/CD pipeline is configured.

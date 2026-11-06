# Sprint 7 — Local owner bootstrap (issue #42)

## Required order

1. Run `make migrate` explicitly before generating a secret. This applies
   Workspaces and then Identity migrations, including `owner_bootstrap`.
2. Start the API (`docker compose up --build -d` when using Docker).
3. On the local administration machine, run:
   `docker compose exec -T api dotnet Alycia.Api.dll bootstrap generate`
   The single output is a **secret**: do not paste it into logs, issues, tickets,
   scripts, environment files, screenshots or Git. Its 64 hex characters represent
   256 cryptographically random bits. It expires after 15 minutes.
4. Present it once to `POST /api/identity/bootstrap` from the API process's
   **own TCP loopback interface**, with JSON fields `secret`, `loginIdentifier`,
   `password`, `passwordConfirmation`. Confirm the normalized password.
5. Only on expiry or five *incorrect nonempty* secret presentations, rerun the
   local admin command to generate a replacement (old token invalidated).
   Missing secrets and a correct secret with invalid form do not consume attempts.
6. After the owner has been created, the command cannot issue more secrets,
   the HTTP bootstrap is permanently closed, and no login session is issued.

## Network boundary and limitation

The server inspects the actual remote socket IP and accepts only loopback;
`Host`/`X-Forwarded-For` do not grant access. This also applies in Production.
An API running in a Docker bridge sees host port-forwarded requests as
**non-loopback** and denies them, as well as other Docker containers. Host Avalonia
access through Docker's published port is **not yet supported**; the local
host/client deployment path must be integrated and tested before AC07/AC08 PASS.
Never disable the loopback guard to make Docker forwarding work.

The API requires `ALYCIA_WORKSPACES_CONNECTION` for a host-local process, or
`ALYCIA_POSTGRES_PASSWORD` in Docker (where host/database/user are fixed in
this development Compose configuration). If neither is available, the
bootstrap routes are disabled. A configured secret must never be logged.
No startup migrations are performed. Existing anonymous Workspace routes remain
unmapped, and `/healthz` remains an unauthenticated, database-free technical
health check.

## Failure modes and privacy

Wrong secrets are counted transactionally. A correct secret and invalid form
leaves the count unchanged. The owner insert, Workspace attachment and secret
consumption use one PostgreSQL transaction. Failures roll back that entire
transaction. The database stores only a SHA-256 verifier for the high-entropy
random secret plus expiry, counter and consumed timestamp. Never log raw
credentials or full PostgreSQL messages. Use only a dedicated isolated database
for manual security/concurrency demonstrations.

This is S7 bootstrap only; S8 login, logout and S9 sessions are **not** present.

## Local negative network check (no secret needed)

After rebuilding and starting the API container, run
`bash scripts/test-s7-bootstrap-peer.sh`. The script issues a request from the
PostgreSQL container using forged forwarding/Host headers and requires HTTP
403. This is a **Docker-peer denial check**, not proof that the host Avalonia
client can reach the loopback-only endpoint. The positive host-local flow will
require its own deployment and end-to-end verification.

# Sprint 7 — Ownership transaction and isolated verification

This document covers issue #41. The approved Sprint 7 SMART is unchanged.

## Migrations

`make migrate` applies the Workspaces migrations first, then the Identity migrations.
Each module maintains its own EF migration history. Migrations are explicit and
idempotent, but the two module deployments are **not** one shared transaction.
The existing Sprint 6 Workspaces migration is never rewritten.

`Workspace.OwnerUserId` initially allows null for pre-bootstrap Sprint 6 rows.
The new Workspace migration adds that column and a nonempty-GUID check without
adding a physical cross-module foreign key. The initialization transaction later
assigns all existing rows, including those created before Sprint 7.

`ALYCIA_MIGRATE_DATABASE` may override the database only for a dedicated
`alycia_s7_test_*` database; without it, `make migrate` still targets the configured owner DB.
Do not run that default migration against the owner's data until the isolated
verification and migration review have passed.

## Atomic initialization

`OwnerInitializationPersistence` lives in the API composition layer; neither
business domain imports the other module's infrastructure. It uses one opened
Npgsql connection and one shared EF Core transaction for the Identity insert
and Workspace assignment. The unique owner-slot constraint prevents a second
owner. A lock on the Workspace table prevents concurrent inserts while rows
are attached. Any exception before commit rolls back both changes.

The persistence operation is **not** exposed as an HTTP route in issue #41.
Issue #42 must validate the local bootstrap secret, password and owner input
before invoking it. The temporary S6 anonymous Workspace endpoint mapping is
removed; normal business authentication remains out of scope until S8.

## Isolated PostgreSQL integration

With the existing PostgreSQL container running, execute:

```bash
bash scripts/test-s7-ownership.sh
```

This creates a **new randomly named** `alycia_s7_test_*` database, installs
only the S6 Workspace baseline first, seeds two existing Workspaces, then runs
`make migrate` twice against that test database. A .NET SDK test container on
the internal Compose network validates an injected database failure and rollback,
safe retry, simultaneous owner attempts, and committed Workspace ownership.
The temporary test database is dropped **only on success**. On failure it is
retained for diagnosis; the script prints its name. No PostgreSQL host port is
published for this test. The test uses an environment-only connection string;
never print Compose's expanded configuration when collecting redacted evidence.

Issue #41 integration tests are deliberately outside `Alycia.slnx` because
they require an explicitly created, migrated test database. Run them using the
script above; `make check` alone does not execute those tests.

#!/usr/bin/env bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

# Explicit, ordered module migrations. Add future modules here, deliberately.
modules=(workspaces)

if [[ -z "$(docker compose ps --status running -q postgres)" ]]; then
    echo "ERROR: PostgreSQL container is not running. Start it explicitly." >&2
    exit 1
fi

for module in "${modules[@]}"; do
    case "$module" in
        workspaces)
            migration_sql=$(mktemp)
            trap 'rm -f "$migration_sql"' EXIT

            dotnet tool run dotnet-ef migrations script \
                --idempotent \
                --project src/Alycia.Modules.Workspaces.Infrastructure \
                --startup-project src/Alycia.Modules.Workspaces.Infrastructure \
                --context WorkspacesDbContext \
                --output "$migration_sql"

            [[ -s "$migration_sql" ]] || {
                echo "ERROR: Generated Workspaces SQL is empty." >&2
                exit 1
            }

            # Runs inside the already-running PostgreSQL container, without
            # exposing port 5432 on the host. psql exits nonzero on SQL errors.
            docker compose exec -T postgres sh -c \
                'exec psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' \
                < "$migration_sql"

            rm -f "$migration_sql"
            trap - EXIT
            ;;
        *)
            echo "ERROR: Unknown migration module: $module" >&2
            exit 1
            ;;
    esac
done

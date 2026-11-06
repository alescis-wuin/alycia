#!/usr/bin/env bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

# Module migrations are ordered and independently idempotent, not jointly atomic.
modules=(workspaces identity)

database_override="${ALYCIA_MIGRATE_DATABASE:-}"
if [[ -n "$database_override" && ! "$database_override" =~ ^alycia_s7_test_[a-z0-9_]+$ ]]; then
    echo "ERROR: Invalid migration database name." >&2
    exit 1
fi

if [[ -z "$(docker compose ps --status running -q postgres)" ]]; then
    echo "ERROR: PostgreSQL container is not running. Start it explicitly." >&2
    exit 1
fi

migration_sql=$(mktemp)
trap 'rm -f "$migration_sql"' EXIT

for module in "${modules[@]}"; do
    case "$module" in
        workspaces)
            project=src/Alycia.Modules.Workspaces.Infrastructure
            context=WorkspacesDbContext
            ;;
        identity)
            project=src/Alycia.Modules.Identity.Infrastructure
            context=IdentityDbContext
            ;;
        *)
            echo "ERROR: Unknown migration module: $module" >&2
            exit 1
            ;;
    esac

    dotnet tool run dotnet-ef migrations script \
        --idempotent \
        --project "$project" \
        --startup-project "$project" \
        --context "$context" \
        --output "$migration_sql"

    [[ -s "$migration_sql" ]] || {
        echo "ERROR: Generated $module SQL is empty." >&2
        exit 1
    }

    # PostgreSQL stays inside Docker; no host port or implicit startup migration.
    docker compose exec -T -e ALYCIA_MIGRATE_DATABASE="$database_override" postgres sh -c \
        'exec psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "${ALYCIA_MIGRATE_DATABASE:-$POSTGRES_DB}"' \
        < "$migration_sql"
done

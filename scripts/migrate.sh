#!/usr/bin/env bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

# Explicit, ordered module migrations. Add future modules here, deliberately.
modules=(workspaces chat)

if [[ -z "$(docker compose ps --status running -q postgres)" ]]; then
    echo "ERROR: PostgreSQL container is not running. Start it explicitly." >&2
    exit 1
fi

for module in "${modules[@]}"; do
    case "$module" in
        workspaces)
            project=src/Alycia.Modules.Workspaces.Infrastructure
            context=WorkspacesDbContext
            ;;
        chat)
            project=src/Alycia.Modules.Chat.Infrastructure
            context=ChatDbContext
            ;;
        *)
            echo "ERROR: Unknown migration module: $module" >&2
            exit 1
            ;;
    esac

    migration_sql=$(mktemp)
    trap 'rm -f "$migration_sql"' EXIT

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

    docker compose exec -T postgres sh -c \
        'exec psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' \
        < "$migration_sql"

    rm -f "$migration_sql"
    trap - EXIT
done

#!/usr/bin/env bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
if [[ $# != 1 ]]; then
    echo "Usage: bash scripts/recette-migration-boreale.sh DOSSIER_DE_SAUVEGARDE" >&2
    exit 2
fi
project="alycia-restore-boreale-$(python3 -c 'import uuid; print(uuid.uuid4().hex[:12])')"
compose=(docker compose -p "$project" -f compose.yaml -f compose.demo.yaml)
export ALYCIA_DEMO_POSTGRES_PORT=0
python3 scripts/backup.py restore "$1" --project "$project"
echo "Projet Compose de recette : $project"
fingerprint_rows() {
    "${compose[@]}" exec -T postgres sh -c \
        'exec psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<'SQL' | sha256sum
SELECT to_jsonb(c) - 'WorkspaceId' FROM chat.conversations c ORDER BY c."Id";
SELECT to_jsonb(m) FROM chat.messages m ORDER BY m."ConversationId", m."Sequence";
SELECT to_jsonb(f) - 'WorkspaceId' FROM chat.attachments f ORDER BY f."Id";
SQL
}
fingerprint_files() {
    "${compose[@]}" run --rm --no-deps -T --entrypoint sh chat-api -c \
        'set -e; cd /app/files; find . -type f -print0 | LC_ALL=C sort -z | xargs -0 -r sha256sum' | sha256sum
}
before_rows=$(fingerprint_rows)
before_files=$(fingerprint_files)
dotnet tool restore
COMPOSE_PROJECT_NAME="$project" bash scripts/migrate.sh
[[ "$(fingerprint_rows)" == "$before_rows" && "$(fingerprint_files)" == "$before_files" ]] || {
    echo "Les contenus historiques de la copie diffèrent après migration." >&2
    exit 1
}
echo "Historiques, mémoires et originaux de la copie identiques avant et après migration."
binding=$("${compose[@]}" port postgres 5432)
export ALYCIA_CHAT_TEST_CONNECTION
ALYCIA_CHAT_TEST_CONNECTION=$("${compose[@]}" config --format json | python3 -c '
import json, sys
from urllib.parse import urlsplit
endpoint = urlsplit("//" + sys.argv[1])
assert endpoint.hostname == "127.0.0.1"
env = json.load(sys.stdin)["services"]["postgres"]["environment"]
def quote(value):
    return "\"" + value.replace("\"", "\"\"") + "\""
print("Host=127.0.0.1;Port=" + str(endpoint.port) + ";Database=postgres;Username=" + quote(env["POSTGRES_USER"]) + ";Password=" + quote(env["POSTGRES_PASSWORD"]))
' "$binding")
bash scripts/check.sh --integration
python3 scripts/tests/http-integration.py --case boreale
echo "Recette terminée. La copie reste consultable dans le projet Compose $project."

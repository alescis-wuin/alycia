#!/usr/bin/env bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

for command in dotnet docker python3; do
    command -v "$command" >/dev/null || { echo "Missing prerequisite: $command" >&2; exit 1; }
done

# Create a secret only for a new checkout. Never replace an existing .env.
if [[ ! -e .env ]]; then
    python3 - <<'PY'
import os
import secrets
with os.fdopen(os.open('.env', os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), 'w') as file:
    file.write('POSTGRES_PASSWORD=' + secrets.token_urlsafe(32) + '\n')
PY
fi

password_state=$(python3 - <<'PY'
from pathlib import Path
text = Path('.env').read_text()
print('placeholder' if 'POSTGRES_PASSWORD=replace-with-a-strong-random-secret' in text else 'ok')
PY
)
if [[ "$password_state" == "placeholder" ]]; then
    echo 'Replace the placeholder POSTGRES_PASSWORD in .env before starting.' >&2
    exit 1
fi
unset password_state

docker compose -f compose.yaml -f compose.demo.yaml up -d --wait postgres
dotnet tool restore
bash scripts/migrate.sh

# Read only the local Compose configuration; never print its password.
ALYCIA_CHAT_CONNECTION=$(python3 - <<'PY'
import json
import subprocess
config = json.loads(subprocess.check_output(
    ['docker', 'compose', '-f', 'compose.yaml', '-f', 'compose.demo.yaml', 'config', '--format', 'json'], text=True))
postgres = config['services']['postgres']
env = postgres['environment']
port = next(item['published'] for item in postgres['ports'] if item['target'] == 5432)
def quote(value):
    return '"' + str(value).replace('"', '""') + '"'
print('Host=127.0.0.1;Port=' + str(port) + ';Database=' + quote(env['POSTGRES_DB'])
      + ';Username=' + quote(env['POSTGRES_USER']) + ';Password=' + quote(env['POSTGRES_PASSWORD'])
      + ';Timeout=15;Command Timeout=15;Include Error Detail=false')
PY
)
export ALYCIA_CHAT_CONNECTION
export ASPNETCORE_ENVIRONMENT=Development
export ALYCIA_ENABLE_LOCAL_CHAT=true
echo 'Starting local chat API on http://127.0.0.1:5187 (Ctrl+C stops the API; PostgreSQL data remains).'
exec dotnet run --no-launch-profile --project src/Alycia.Api -- --urls http://127.0.0.1:5187

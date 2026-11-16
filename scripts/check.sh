#!/usr/bin/env bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
if [[ "${1:-}" != "" && "${1:-}" != "--integration" ]]; then
    echo 'Usage: bash scripts/check.sh [--integration]' >&2
    exit 2
fi
if [[ "${1:-}" == "--integration" && -z "${ALYCIA_CHAT_TEST_CONNECTION:-}" ]]; then
    echo 'ALYCIA_CHAT_TEST_CONNECTION is required (test administrator with CREATEDB).' >&2
    exit 2
fi
dotnet restore Alycia.slnx --disable-parallel -m:1
dotnet tool restore
dotnet build Alycia.slnx --no-restore -m:1 -p:UseSharedCompilation=false
bash scripts/quality.sh lint
dotnet tool run dotnet-ef migrations has-pending-model-changes \
    --project src/Alycia.Modules.Chat.Infrastructure --context ChatDbContext --no-build
dotnet test Alycia.slnx --no-build --no-restore -m:1 \
    --logger trx --results-directory artifacts/tests
if [[ -z "${ALYCIA_CHAT_TEST_CONNECTION:-}" ]]; then
    echo 'PostgreSQL integration tests were skipped; use --integration with a test connection.'
fi

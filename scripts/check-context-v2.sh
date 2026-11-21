#!/usr/bin/env bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
if [[ $# -gt 1 || (${1:-} != "" && ${1:-} != "--pgvector") ]]; then
    echo "Usage : bash scripts/check-context-v2.sh [--pgvector]" >&2
    exit 2
fi
if [[ ${1:-} == "--pgvector" ]]; then export ALYCIA_TEST_PGVECTOR=1; fi

# A supplied test server is owned by the caller. Otherwise create a disposable instance.
if [[ -z "${ALYCIA_CHAT_TEST_CONNECTION:-}" ]]; then
    image=postgres:17-alpine
    if [[ ${ALYCIA_TEST_PGVECTOR:-0} == 1 ]]; then
        image=alycia-postgres-pgvector:17-0.8.7
        if ! docker image inspect "$image" >/dev/null 2>&1; then
            docker build -f docker/postgres-pgvector.Dockerfile -t "$image" .
        fi
    fi
    export POSTGRES_PASSWORD
    POSTGRES_PASSWORD=$(python3 -c 'import secrets; print(secrets.token_urlsafe(32))')
    container=$(docker run --rm -d -p 127.0.0.1::5432 -e POSTGRES_PASSWORD -e POSTGRES_USER=alycia_test "$image")
    trap 'docker stop "$container" >/dev/null' EXIT
    for _ in {1..60}; do
        if docker exec "$container" pg_isready -h 127.0.0.1 -U alycia_test >/dev/null; then break; fi
        sleep 1
    done
    docker exec "$container" pg_isready -h 127.0.0.1 -U alycia_test >/dev/null
    binding=$(docker port "$container" 5432/tcp)
    export ALYCIA_CHAT_TEST_CONNECTION="Host=127.0.0.1;Port=${binding##*:};Database=postgres;Username=alycia_test;Password=$POSTGRES_PASSWORD"
    unset POSTGRES_PASSWORD
fi

bash scripts/check.sh --integration
python3 -m unittest discover -s scripts/tests -p '*_tests.py'
python3 scripts/tests/http-integration.py --case all
dotnet build benchmarks/Alycia.ContextBenchmarks -m:1 -p:UseSharedCompilation=false
mkdir -p artifacts/evaluation
run=$(mktemp -d artifacts/evaluation/context-v2-XXXXXXXX)
python3 scripts/evaluate-context.py offline --pipeline-v2 --output "$run/resultats.json"
echo "Mesures disponibles dans $run/resultats.json"

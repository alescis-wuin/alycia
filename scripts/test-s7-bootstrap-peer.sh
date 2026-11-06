#!/usr/bin/env bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

[[ -n "$(docker compose ps --status running -q api)" ]] || {
    echo "ERROR: Start the built API container before the Docker-peer check." >&2
    exit 1
}
[[ -n "$(docker compose ps --status running -q postgres)" ]] || {
    echo "ERROR: PostgreSQL container must be running as the Docker peer." >&2
    exit 1
}

# Alpine's BusyBox wget checks HTTP from another container on the same bridge.
# No bootstrap secret or database writes are involved.
response=$(docker compose exec -T postgres sh -c \
    'wget -S -O /dev/null --header="Host: localhost" \
      --header="X-Forwarded-For: 127.0.0.1" \
      http://api:8080/api/identity/bootstrap/status 2>&1' || true)

if [[ "$response" != *"403 Forbidden"* && "$response" != *"HTTP/1.1 403"* ]]; then
    echo "ERROR: Docker peer was not explicitly denied with HTTP 403." >&2
    printf '%s\n' "$response" >&2
    exit 1
fi

echo "S7 Docker-peer HTTP denial: 403 (spoofed headers ignored)."

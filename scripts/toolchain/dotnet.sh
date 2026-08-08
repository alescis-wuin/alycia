#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(
    cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &&
        pwd
)"
readonly SCRIPT_DIR

# shellcheck source=scripts/toolchain/lib.sh
source "$SCRIPT_DIR/lib.sh"

resolved_dotnet="$("$SCRIPT_DIR/resolve-dotnet.sh" --path)"

if alicia_is_repository_local_dotnet "$resolved_dotnet"; then
    export DOTNET_ROOT="$ALICIA_REPOSITORY_ROOT/.dotnet"
    export DOTNET_MULTILEVEL_LOOKUP=0
    export PATH="$DOTNET_ROOT:$PATH"
fi

export DOTNET_CLI_TELEMETRY_OPTOUT="${DOTNET_CLI_TELEMETRY_OPTOUT:-1}"
export DOTNET_NOLOGO="${DOTNET_NOLOGO:-1}"

exec "$resolved_dotnet" "$@"

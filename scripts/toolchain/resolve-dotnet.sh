#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(
    cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &&
        pwd
)"
readonly SCRIPT_DIR

# shellcheck source=scripts/toolchain/lib.sh
source "$SCRIPT_DIR/lib.sh"

output_mode='path'

usage()
{
    cat <<'EOF'
Usage: resolve-dotnet.sh [--path|--source|--version|--json]

Resolve a compatible .NET SDK host in this order:
  1. ALICIA_DOTNET override, when explicitly set;
  2. repository-local .dotnet/dotnet;
  3. compatible dotnet available from PATH.
EOF
}

while (($# > 0)); do
    case "$1" in
        --path)
            output_mode='path'
            ;;
        --source)
            output_mode='source'
            ;;
        --version)
            output_mode='version'
            ;;
        --json)
            output_mode='json'
            ;;
        --help | -h)
            usage
            exit 0
            ;;
        *)
            alicia_toolchain_error "Unsupported argument: $1"
            usage >&2
            exit 2
            ;;
    esac

    shift
done

required_version="$(alicia_required_sdk_version)"
roll_forward="$(alicia_sdk_roll_forward_policy)"
resolved_path=''
resolved_source=''
resolved_version=''

consider_candidate()
{
    local candidate="$1"
    local source_name="$2"
    local candidate_root="${3:-}"
    local version=''

    [[ -x "$candidate" ]] ||
        return 1

    if ! version="$(
        alicia_dotnet_version \
            "$candidate" \
            "$candidate_root" \
            2>/dev/null
    )"; then
        return 1
    fi

    if ! alicia_sdk_version_is_compatible \
        "$required_version" \
        "$version" \
        "$roll_forward"; then
        return 1
    fi

    resolved_path="$(realpath -- "$candidate")"
    resolved_source="$source_name"
    resolved_version="$version"

    return 0
}

if [[ -n "${ALICIA_DOTNET:-}" ]]; then
    override_path="$(realpath -m -- "$ALICIA_DOTNET")"
    override_root=''

    if alicia_is_repository_local_dotnet "$override_path"; then
        override_root="$ALICIA_REPOSITORY_ROOT/.dotnet"
    fi

    consider_candidate \
        "$override_path" \
        'override' \
        "$override_root" || {
        alicia_toolchain_error \
            "ALICIA_DOTNET does not provide a compatible SDK: $override_path"
        exit 1
    }
fi

if [[ -z "$resolved_path" ]]; then
    local_candidate="$ALICIA_REPOSITORY_ROOT/.dotnet/dotnet"

    consider_candidate \
        "$local_candidate" \
        'repository-local' \
        "$ALICIA_REPOSITORY_ROOT/.dotnet" || true
fi

if [[ -z "$resolved_path" && \
    "${ALICIA_DOTNET_DISABLE_SYSTEM:-0}" != '1' ]]; then
    system_candidate="$(command -v dotnet || true)"

    if [[ -n "$system_candidate" ]]; then
        consider_candidate \
            "$system_candidate" \
            'system' || true
    fi
fi

if [[ -z "$resolved_path" ]]; then
    alicia_toolchain_error \
        "No compatible .NET SDK was found for $required_version ($roll_forward)."
    alicia_toolchain_error \
        "Run scripts/toolchain/bootstrap-dotnet.sh to install the repository-local SDK."
    exit 1
fi

case "$output_mode" in
    path)
        printf '%s\n' "$resolved_path"
        ;;
    source)
        printf '%s\n' "$resolved_source"
        ;;
    version)
        printf '%s\n' "$resolved_version"
        ;;
    json)
        python3 - \
            "$resolved_path" \
            "$resolved_source" \
            "$resolved_version" \
            "$required_version" \
            "$roll_forward" <<'PYTHON'
import json
import sys

path, source, version, required, policy = sys.argv[1:]
print(json.dumps({
    "path": path,
    "source": source,
    "version": version,
    "required_version": required,
    "roll_forward": policy,
}, sort_keys=True))
PYTHON
        ;;
esac

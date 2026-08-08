#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(
    cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &&
        pwd
)"
readonly SCRIPT_DIR

# shellcheck source=scripts/toolchain/lib.sh
source "$SCRIPT_DIR/lib.sh"

candidate=''
quiet=0

usage()
{
    cat <<'EOF'
Usage: verify-dotnet.sh [--path FILE] [--quiet]

Verify that a dotnet host selects an SDK compatible with global.json.
EOF
}

while (($# > 0)); do
    case "$1" in
        --path)
            shift
            (($# > 0)) || {
                alicia_toolchain_error '--path requires a value.'
                exit 2
            }
            candidate="$1"
            ;;
        --quiet)
            quiet=1
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

if [[ -z "$candidate" ]]; then
    candidate="$("$SCRIPT_DIR/resolve-dotnet.sh" --path)"
fi

candidate="$(realpath -m -- "$candidate")"

[[ -x "$candidate" ]] || {
    alicia_toolchain_error \
        "dotnet host is not executable: $candidate"
    exit 1
}

required_version="$(alicia_required_sdk_version)"
roll_forward="$(alicia_sdk_roll_forward_policy)"
candidate_root=''

if alicia_is_repository_local_dotnet "$candidate"; then
    candidate_root="$ALICIA_REPOSITORY_ROOT/.dotnet"
elif [[ "$(dirname -- "$candidate")" == \
    "$(realpath -m -- "$ALICIA_REPOSITORY_ROOT/.dotnet")" ]]; then
    candidate_root="$(dirname -- "$candidate")"
fi

if ! selected_version="$(
    alicia_dotnet_version \
        "$candidate" \
        "$candidate_root"
)"; then
    alicia_toolchain_error \
        "Unable to query the selected SDK from $candidate"
    exit 1
fi

if ! alicia_sdk_version_is_compatible \
    "$required_version" \
    "$selected_version" \
    "$roll_forward"; then
    alicia_toolchain_error \
        "Selected SDK $selected_version is incompatible with $required_version ($roll_forward)."
    exit 1
fi

if ((quiet == 0)); then
    alicia_toolchain_success \
        "Compatible .NET SDK selected: $selected_version"
    alicia_toolchain_info \
        "dotnet host: $candidate"
fi

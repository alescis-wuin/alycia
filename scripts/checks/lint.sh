#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIRECTORY="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly SCRIPT_DIRECTORY
REPOSITORY_ROOT="$(
    cd -- "$SCRIPT_DIRECTORY/../.." &&
        pwd
)"
readonly REPOSITORY_ROOT
readonly COMMON_SCRIPT="$REPOSITORY_ROOT/scripts/lib/common.sh"

# shellcheck disable=SC1090
source "$COMMON_SCRIPT"

section "Shell lint"
require_command shellcheck

mapfile -d '' shell_files < <(
    find "$REPOSITORY_ROOT/scripts" "$REPOSITORY_ROOT/.githooks" \
        -type f ! -name 'README.md' -print0 |
        while IFS= read -r -d '' candidate; do
            first_line=''

            IFS= read -r first_line <"$candidate" || true

            if [[ "$candidate" == *.sh ||
                "$first_line" =~ ^\#\!.*(bash|dash|ksh|sh|zsh)([[:space:]]|$) ]]
            then
                printf '%s\0' "$candidate"
            fi
        done
)

if ((${#shell_files[@]} == 0)); then
    warning "No shell files were found."
    exit 0
fi

shellcheck "${shell_files[@]}"
success "ShellCheck completed without warnings for ${#shell_files[@]} file(s)."

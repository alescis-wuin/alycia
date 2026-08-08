#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIRECTORY="$(
    cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &&
        pwd
)"
readonly SCRIPT_DIRECTORY

DEFAULT_REPOSITORY_ROOT="$(
    cd -- "$SCRIPT_DIRECTORY/../.." &&
        pwd
)"
readonly DEFAULT_REPOSITORY_ROOT

readonly COMMON_SCRIPT="$DEFAULT_REPOSITORY_ROOT/scripts/lib/common.sh"

# shellcheck disable=SC1090
source "$COMMON_SCRIPT"

requested_root="${1:-$DEFAULT_REPOSITORY_ROOT}"

repository_root="$(
    git -C "$requested_root" rev-parse --show-toplevel 2>/dev/null
)" ||
    die "Repository root is not a Git worktree: $requested_root"

readonly repository_root

section "Worktree cleanliness"

status_output="$(
    git -C "$repository_root" status \
        --porcelain=v1 \
        --untracked-files=all \
        --ignore-submodules=none
)"

if [[ -n "$status_output" ]]; then
    failure "Uncommitted repository changes were detected:"

    while IFS= read -r status_line; do
        printf '  %s\n' "$status_line" >&2
    done <<<"$status_output"

    die "Push rejected: commit, stash, or remove every pending change before pushing."
fi

success "The worktree and index are clean."

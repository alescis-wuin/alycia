#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIRECTORY="$(
    cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &&
        pwd
)"
readonly SCRIPT_DIRECTORY

REPOSITORY_ROOT="$(
    cd -- "$SCRIPT_DIRECTORY/../.." &&
        pwd
)"
readonly REPOSITORY_ROOT

readonly COMMON_SCRIPT="$REPOSITORY_ROOT/scripts/lib/common.sh"

# shellcheck disable=SC1090
source "$COMMON_SCRIPT"

section "Post-commit verification"

if git -C "$REPOSITORY_ROOT" verify-commit HEAD; then
    success "The new commit has a valid signature."
else
    failure "The new commit signature could not be verified."
fi

env GIT_PAGER=cat git -C "$REPOSITORY_ROOT" \
    log \
    --show-signature \
    --format=fuller \
    -1

status_output="$(
    git -C "$REPOSITORY_ROOT" status --short
)"

if [[ -n "$status_output" ]]; then
    warning "The worktree contains remaining changes."
    printf '%s\n' "$status_output"
else
    success "The worktree is clean."
fi

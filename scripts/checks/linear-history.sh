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
readonly BASE_REF="${BASE_REF:-origin/develop}"

# shellcheck disable=SC1090
source "$COMMON_SCRIPT"

section "Linear branch history"

git -C "$REPOSITORY_ROOT" rev-parse --verify "$BASE_REF" >/dev/null 2>&1 ||
    die "Base reference not found: $BASE_REF"

merge_base="$(git -C "$REPOSITORY_ROOT" merge-base HEAD "$BASE_REF")"
merge_commits="$(git -C "$REPOSITORY_ROOT" rev-list --merges "$merge_base..HEAD")"

if [[ -n "$merge_commits" ]]; then
    git -C "$REPOSITORY_ROOT" log --oneline --decorate \
        "$merge_base..HEAD" --merges >&2
    die "Merge commits are not allowed on work branches."
fi

success "Work branch history is linear relative to $BASE_REF."

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

section "Branch policy"

branch_name="$(git -C "$REPOSITORY_ROOT" branch --show-current)"

[[ -n "$branch_name" ]] ||
    die "Detached HEAD is not allowed for repository workflow commands."

if is_protected_branch "$branch_name"; then
    die "Direct work on protected branch '$branch_name' is not allowed."
fi

readonly branch_pattern='^(feature|fix|chore|test|docs|refactor|perf|build|ci|style|release|hotfix)/[a-z0-9][a-z0-9-]*$'

[[ "$branch_name" =~ $branch_pattern ]] ||
    die "Invalid branch name: $branch_name"

success "Branch name follows repository conventions: $branch_name"

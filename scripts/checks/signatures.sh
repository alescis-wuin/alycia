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

section "Commit signatures"

git -C "$REPOSITORY_ROOT" rev-parse --verify "$BASE_REF" >/dev/null 2>&1 ||
    die "Base reference not found: $BASE_REF"

merge_base="$(git -C "$REPOSITORY_ROOT" merge-base HEAD "$BASE_REF")"

mapfile -t commit_shas < <(
    git -C "$REPOSITORY_ROOT" rev-list --reverse "$merge_base..HEAD"
)

if ((${#commit_shas[@]} == 0)); then
    warning "No branch commits require signature verification."
    exit 0
fi

for commit_sha in "${commit_shas[@]}"; do
    git -C "$REPOSITORY_ROOT" verify-commit "$commit_sha" >/dev/null
    success "Valid signature: $commit_sha"
done

success "All branch commits have valid signatures."

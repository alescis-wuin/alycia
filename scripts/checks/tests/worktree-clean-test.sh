#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIRECTORY="$(
    cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &&
        pwd
)"
readonly SCRIPT_DIRECTORY

REPOSITORY_ROOT="$(
    cd -- "$SCRIPT_DIRECTORY/../../.." &&
        pwd
)"
readonly REPOSITORY_ROOT

readonly COMMON_SCRIPT="$REPOSITORY_ROOT/scripts/lib/common.sh"
readonly CHECK_SCRIPT="$REPOSITORY_ROOT/scripts/checks/worktree-clean.sh"
readonly PRE_PUSH_SCRIPT="$REPOSITORY_ROOT/scripts/git/pre-push.sh"
readonly MAKEFILE="$REPOSITORY_ROOT/Makefile"

# shellcheck disable=SC1090
source "$COMMON_SCRIPT"

section "Worktree cleanliness tests"

grep -Fq \
    "verify-push" \
    "$PRE_PUSH_SCRIPT" ||
    die "The pre-push hook does not invoke verify-push."

grep -Fq \
    "worktree-clean: ##" \
    "$MAKEFILE" ||
    die "The Makefile does not expose worktree-clean."

success "Pre-push integration uses the clean-state quality gate."

temporary_repository="$(mktemp -d)"
readonly temporary_repository

cleanup()
{
    rm -rf -- "$temporary_repository"
}
trap cleanup EXIT

git -C "$temporary_repository" init --quiet
git -C "$temporary_repository" config user.name "Alicia Tests"
git -C "$temporary_repository" config user.email "tests@alicia.local"
git -C "$temporary_repository" config core.hooksPath /dev/null

printf 'baseline\n' >"$temporary_repository/tracked.txt"
printf 'ignored.txt\n' >"$temporary_repository/.gitignore"

git -C "$temporary_repository" add -- .gitignore tracked.txt
git -C "$temporary_repository" commit \
    --quiet \
    --no-gpg-sign \
    --message "test: initialize worktree fixture"

run_clean_check()
{
    NO_COLOR=1 "$CHECK_SCRIPT" "$temporary_repository"
}

expect_clean()
{
    local label="$1"
    local output exit_code

    set +e
    output="$(run_clean_check 2>&1)"
    exit_code="$?"
    set -e

    if ((exit_code != 0)); then
        failure "$label unexpectedly failed."
        printf '%s\n' "$output" >&2
        exit 1
    fi

    grep -Fq \
        "[OK] The worktree and index are clean." \
        <<<"$output" ||
        die "$label did not report the expected clean status."

    success "$label"
}

expect_dirty()
{
    local label="$1"
    local expected_status="$2"
    local output exit_code

    set +e
    output="$(run_clean_check 2>&1)"
    exit_code="$?"
    set -e

    ((exit_code != 0)) ||
        die "$label unexpectedly accepted a dirty repository."

    grep -Fq "$expected_status" <<<"$output" ||
        die "$label did not report status '$expected_status'."

    grep -Fq \
        "[ERROR] Push rejected: commit, stash, or remove every pending change before pushing." \
        <<<"$output" ||
        die "$label did not report the expected rejection."

    success "$label"
}

expect_clean "Clean repository is accepted."

printf 'unstaged\n' >>"$temporary_repository/tracked.txt"
expect_dirty \
    "Unstaged tracked changes are rejected." \
    " M tracked.txt"
git -C "$temporary_repository" restore -- tracked.txt

printf 'staged\n' >>"$temporary_repository/tracked.txt"
git -C "$temporary_repository" add -- tracked.txt
expect_dirty \
    "Staged changes are rejected." \
    "M  tracked.txt"
git -C "$temporary_repository" reset --hard --quiet HEAD

printf 'untracked\n' >"$temporary_repository/untracked.txt"
expect_dirty \
    "Untracked files are rejected." \
    "?? untracked.txt"
rm -- "$temporary_repository/untracked.txt"

printf 'ignored\n' >"$temporary_repository/ignored.txt"
expect_clean "Ignored files do not block a push."

success "Worktree cleanliness tests completed."

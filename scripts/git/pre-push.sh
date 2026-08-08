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
readonly REMOTE_NAME="${1:-unknown}"
readonly REMOTE_URL="${2:-unknown}"

# shellcheck disable=SC1090
source "$COMMON_SCRIPT"

section "Pre-push verification"

info "Remote name: $REMOTE_NAME"
info "Remote URL: $REMOTE_URL"

has_non_deletion_update=false
update_count=0

while read -r local_ref local_oid remote_ref remote_oid; do
    [[ -n "${local_ref:-}" ]] ||
        continue

    update_count=$((update_count + 1))

    info "Push update: $local_ref -> $remote_ref"

    case "$remote_ref" in
        refs/heads/main | refs/heads/testing | refs/heads/develop)
            die "Direct pushes to protected branch '$remote_ref' are not allowed."
            ;;
    esac

    if [[ ! "$local_oid" =~ ^0+$ ]]; then
        has_non_deletion_update=true
    fi

    # Keep all values consumed and documented for Git hook compatibility.
    : "$remote_oid"
done

if [[ "${PRE_PUSH_DRY_RUN:-0}" == "1" && $update_count -eq 0 ]]; then
    warning "Dry-run mode has no ref updates; running the quality gate only."
    has_non_deletion_update=true
fi

if [[ "$has_non_deletion_update" == "false" ]]; then
    make -C "$REPOSITORY_ROOT" \
        --no-print-directory \
        worktree-clean

    success "Only non-protected branch deletions are being pushed."
    exit 0
fi

make -C "$REPOSITORY_ROOT" \
    --no-print-directory \
    verify-push

success "Pre-push quality gate completed."

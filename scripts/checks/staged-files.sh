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
readonly MAX_STAGED_FILE_SIZE_BYTES="$((5 * 1024 * 1024))"

# shellcheck disable=SC1090
source "$COMMON_SCRIPT"

section "Staged files"

mapfile -d '' staged_files < <(
    git -C "$REPOSITORY_ROOT" diff --cached \
        --name-only --diff-filter=ACMR -z
)

if ((${#staged_files[@]} == 0)); then
    warning "No staged files were found."
    exit 0
fi

[[ -z "$(git -C "$REPOSITORY_ROOT" diff --cached --name-only --diff-filter=U)" ]] ||
    die "Unresolved merge conflicts are staged."

for file_path in "${staged_files[@]}"; do
    case "$file_path" in
        bin/* | */bin/* | obj/* | */obj/* | artifacts/* | */artifacts/* | \
        .idea/* | */.idea/* | .vs/* | */.vs/*)
            die "Forbidden generated path is staged: $file_path"
            ;;
        .env | .env.*)
            [[ "$file_path" == ".env.example" ]] ||
                die "Sensitive environment file is staged: $file_path"
            ;;
        *.pem | *.p12 | *.pfx | *.key | id_rsa | id_ed25519)
            die "Potential private key or certificate is staged: $file_path"
            ;;
    esac

    blob_size="$(git -C "$REPOSITORY_ROOT" cat-file -s ":$file_path")"

    ((blob_size <= MAX_STAGED_FILE_SIZE_BYTES)) ||
        die "Staged file exceeds 5 MiB: $file_path"

    success "Staged file accepted: $file_path"
done

if git -C "$REPOSITORY_ROOT" grep --cached -n -E \
    '^(<<<<<<< |=======|>>>>>>> )' -- . >/dev/null 2>&1
then
    git -C "$REPOSITORY_ROOT" grep --cached -n -E \
        '^(<<<<<<< |=======|>>>>>>> )' -- . >&2
    die "Conflict markers were found in staged content."
fi

success "Staged file safety checks completed."

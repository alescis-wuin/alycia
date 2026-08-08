#!/usr/bin/env bash
set -Eeuo pipefail

PACKAGE_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPOSITORY_ROOT="${1:-$PWD}"
SOURCE_ROOT="$PACKAGE_ROOT/files"

[[ -d "$REPOSITORY_ROOT/.git" ]] || {
    printf 'Repository root is not a Git worktree: %s\n' "$REPOSITORY_ROOT" >&2
    exit 1
}

while IFS= read -r -d '' source_file; do
    relative_path="${source_file#"$SOURCE_ROOT"/}"
    destination="$REPOSITORY_ROOT/$relative_path"
    mkdir -p -- "$(dirname -- "$destination")"
    cp -- "$source_file" "$destination"
    printf 'Updated %s\n' "$relative_path"
done < <(find "$SOURCE_ROOT" -type f -print0)

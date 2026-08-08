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
readonly HOOKS_DIRECTORY="$REPOSITORY_ROOT/.githooks"

# The path is resolved dynamically from the active worktree.
# shellcheck disable=SC1090
source "$COMMON_SCRIPT"

check_hooks()
{
    section "Git hooks configuration"

    local configured_path

    if [[ "$(
        git -C "$REPOSITORY_ROOT" \
            config --bool --get extensions.worktreeConfig ||
            true
    )" == "true" ]]; then
        configured_path="$(
            git -C "$REPOSITORY_ROOT" \
                config --worktree --get core.hooksPath ||
                true
        )"
    else
        configured_path=""
    fi

    [[ "$configured_path" == "$HOOKS_DIRECTORY" ]] ||
        die "core.hooksPath is not configured as $HOOKS_DIRECTORY."

    success "Worktree-local Git hooks path: $configured_path"

    local hook_file

    while IFS= read -r -d '' hook_file; do
        [[ -x "$hook_file" ]] ||
            die "Hook is not executable: $hook_file"

        success "Executable hook: ${hook_file#"$REPOSITORY_ROOT/"}"
    done < <(
        find "$HOOKS_DIRECTORY" \
            -maxdepth 1 \
            -type f \
            ! -name 'README.md' \
            -print0
    )
}

install_hooks()
{
    section "Installing Git hooks"

    git -C "$REPOSITORY_ROOT" \
        config extensions.worktreeConfig true

    git -C "$REPOSITORY_ROOT" \
        config --worktree core.hooksPath "$HOOKS_DIRECTORY"

    find "$HOOKS_DIRECTORY" \
        -maxdepth 1 \
        -type f \
        ! -name 'README.md' \
        -exec chmod 0755 {} +

    success "Enabled worktree-specific Git configuration."
    success "Configured core.hooksPath as $HOOKS_DIRECTORY."

    check_hooks
}

case "${1:-install}" in
    install)
        install_hooks
        ;;
    check | --check)
        check_hooks
        ;;
    *)
        die "Usage: $0 [install|check|--check]"
        ;;
esac

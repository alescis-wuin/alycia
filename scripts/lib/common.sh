#!/usr/bin/env bash

if [[ -t 1 && -z "${NO_COLOR:-}" ]]; then
    readonly COLOR_RESET=$'\033[0m'
    readonly COLOR_BOLD=$'\033[1m'
    readonly COLOR_RED=$'\033[31m'
    readonly COLOR_GREEN=$'\033[32m'
    readonly COLOR_YELLOW=$'\033[33m'
    readonly COLOR_BLUE=$'\033[34m'
    readonly COLOR_CYAN=$'\033[36m'
else
    readonly COLOR_RESET=''
    readonly COLOR_BOLD=''
    readonly COLOR_RED=''
    readonly COLOR_GREEN=''
    readonly COLOR_YELLOW=''
    readonly COLOR_BLUE=''
    readonly COLOR_CYAN=''
fi

log_message()
{
    local level="$1"
    local color="$2"

    shift 2

    printf '%b[%s]%b %s\n' \
        "${color}${COLOR_BOLD}" \
        "$level" \
        "$COLOR_RESET" \
        "$*"
}

info()
{
    log_message "INFO" "$COLOR_BLUE" "$*"
}

success()
{
    log_message "OK" "$COLOR_GREEN" "$*"
}

warning()
{
    log_message "WARN" "$COLOR_YELLOW" "$*"
}

failure()
{
    log_message "ERROR" "$COLOR_RED" "$*" >&2
}

section()
{
    printf '\n%b=== %s ===%b\n' \
        "${COLOR_BOLD}${COLOR_CYAN}" \
        "$*" \
        "$COLOR_RESET"
}

die()
{
    failure "$*"
    exit 1
}

require_command()
{
    local command_name="$1"

    command -v "$command_name" >/dev/null 2>&1 ||
        die "Required command not found: $command_name"

    success "Command available: $command_name"
}

require_file()
{
    local file_path="$1"

    [[ -f "$file_path" ]] ||
        die "Required file not found: $file_path"

    success "File available: $file_path"
}

repository_root()
{
    git rev-parse --show-toplevel
}

current_branch()
{
    git branch --show-current
}

is_protected_branch()
{
    local branch_name="$1"

    case "$branch_name" in
        main | testing | develop)
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

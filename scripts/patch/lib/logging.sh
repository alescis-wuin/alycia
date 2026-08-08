#!/usr/bin/env bash

if [[ -t 1 && -z "${NO_COLOR:-}" ]]; then
    readonly PATCH_COLOR_RESET=$'\033[0m'
    readonly PATCH_COLOR_BOLD=$'\033[1m'
    readonly PATCH_COLOR_RED=$'\033[31m'
    readonly PATCH_COLOR_GREEN=$'\033[32m'
    readonly PATCH_COLOR_YELLOW=$'\033[33m'
    readonly PATCH_COLOR_BLUE=$'\033[34m'
    readonly PATCH_COLOR_MAGENTA=$'\033[35m'
    readonly PATCH_COLOR_CYAN=$'\033[36m'
else
    readonly PATCH_COLOR_RESET=''
    readonly PATCH_COLOR_BOLD=''
    readonly PATCH_COLOR_RED=''
    readonly PATCH_COLOR_GREEN=''
    readonly PATCH_COLOR_YELLOW=''
    readonly PATCH_COLOR_BLUE=''
    readonly PATCH_COLOR_MAGENTA=''
    readonly PATCH_COLOR_CYAN=''
fi

PATCH_CURRENT_STEP=0
PATCH_TOTAL_STEPS=1
PATCH_CURRENT_STAGE='INIT'
PATCH_STARTED_AT_EPOCH="$(date +%s)"

patch_logging_init()
{
    local log_directory="$1"

    PATCH_LOG_DIRECTORY="$log_directory"
    PATCH_CONSOLE_LOG="$PATCH_LOG_DIRECTORY/console.log"
    PATCH_EVENT_LOG="$PATCH_LOG_DIRECTORY/events.jsonl"
    PATCH_COMMAND_LOG_DIRECTORY="$PATCH_LOG_DIRECTORY/commands"
    PATCH_SNAPSHOT_DIRECTORY="$PATCH_LOG_DIRECTORY/snapshots"

    export PATCH_LOG_DIRECTORY PATCH_CONSOLE_LOG PATCH_EVENT_LOG
    export PATCH_COMMAND_LOG_DIRECTORY PATCH_SNAPSHOT_DIRECTORY

    mkdir -p -- \
        "$PATCH_COMMAND_LOG_DIRECTORY" \
        "$PATCH_SNAPSHOT_DIRECTORY"

    : >"$PATCH_CONSOLE_LOG"
    : >"$PATCH_EVENT_LOG"
}

patch_json_escape()
{
    local value="$1"

    value="${value//\\/\\\\}"
    value="${value//\"/\\\"}"
    value="${value//$'\n'/\\n}"
    value="${value//$'\r'/\\r}"
    value="${value//$'\t'/\\t}"

    printf '%s' "$value"
}

patch_event()
{
    local level="$1"
    local stage="$2"
    local message="$3"
    local timestamp

    timestamp="$(date --iso-8601=seconds)"

    printf '{"timestamp":"%s","step":%d,"total_steps":%d,"stage":"%s","level":"%s","message":"%s"}\n' \
        "$(patch_json_escape "$timestamp")" \
        "$PATCH_CURRENT_STEP" \
        "$PATCH_TOTAL_STEPS" \
        "$(patch_json_escape "$stage")" \
        "$(patch_json_escape "$level")" \
        "$(patch_json_escape "$message")" \
        >>"$PATCH_EVENT_LOG"
}

patch_level_color()
{
    case "$1" in
        INFO) printf '%s' "$PATCH_COLOR_BLUE" ;;
        OK) printf '%s' "$PATCH_COLOR_GREEN" ;;
        WARN) printf '%s' "$PATCH_COLOR_YELLOW" ;;
        ERROR) printf '%s' "$PATCH_COLOR_RED" ;;
        CMD) printf '%s' "$PATCH_COLOR_MAGENTA" ;;
        *) printf '%s' "$PATCH_COLOR_CYAN" ;;
    esac
}

patch_log()
{
    local level="$1"
    local message="$2"
    local timestamp progress elapsed color plain_prefix color_prefix

    timestamp="$(date '+%Y-%m-%d %H:%M:%S')"
    elapsed="$(( $(date +%s) - PATCH_STARTED_AT_EPOCH ))"
    progress="$(( PATCH_CURRENT_STEP * 100 / PATCH_TOTAL_STEPS ))"
    color="$(patch_level_color "$level")"
    plain_prefix="[$timestamp][PATCH][$PATCH_CURRENT_STEP/$PATCH_TOTAL_STEPS][$progress%][$PATCH_CURRENT_STAGE][$level][+${elapsed}s]"
    color_prefix="${PATCH_COLOR_BOLD}${PATCH_COLOR_CYAN}[$timestamp][PATCH]${PATCH_COLOR_RESET}${PATCH_COLOR_BOLD}[$PATCH_CURRENT_STEP/$PATCH_TOTAL_STEPS][$progress%][$PATCH_CURRENT_STAGE]${PATCH_COLOR_RESET}${color}${PATCH_COLOR_BOLD}[$level]${PATCH_COLOR_RESET}${PATCH_COLOR_CYAN}[+${elapsed}s]${PATCH_COLOR_RESET}"

    printf '%b %s\n' "$color_prefix" "$message"
    printf '%s %s\n' "$plain_prefix" "$message" >>"$PATCH_CONSOLE_LOG"
    patch_event "$level" "$PATCH_CURRENT_STAGE" "$message"
}

patch_info() { patch_log INFO "$*"; }
patch_success() { patch_log OK "$*"; }
patch_warning() { patch_log WARN "$*"; }
patch_error() { patch_log ERROR "$*" >&2; }

patch_step()
{
    PATCH_CURRENT_STEP="$((PATCH_CURRENT_STEP + 1))"
    PATCH_CURRENT_STAGE="$1"
    shift

    printf '\n'
    patch_log INFO "$*"
}

patch_command_line()
{
    local command_name="$1"
    local line="$2"
    local timestamp prefix

    timestamp="$(date '+%Y-%m-%d %H:%M:%S')"
    prefix="[$timestamp][PATCH][$PATCH_CURRENT_STEP/$PATCH_TOTAL_STEPS][$PATCH_CURRENT_STAGE][CMD:$command_name]"

    printf '%b%s%b %s\n' \
        "${PATCH_COLOR_MAGENTA}${PATCH_COLOR_BOLD}" \
        "$prefix" \
        "$PATCH_COLOR_RESET" \
        "$line"
    printf '%s %s\n' "$prefix" "$line" >>"$PATCH_CONSOLE_LOG"
}

patch_sanitize_name()
{
    printf '%s' "$1" | tr -cs '[:alnum:]_.-' '_'
}

patch_run_command()
{
    local command_name="$1"
    local command_text="$2"
    local working_directory="${3:-$PWD}"
    local command_log safe_name exit_code

    safe_name="$(patch_sanitize_name "$command_name")"
    command_log="$PATCH_COMMAND_LOG_DIRECTORY/$(printf '%02d' "$PATCH_CURRENT_STEP")-$safe_name.log"

    patch_info "Running command '$command_name': $command_text"
    printf 'Working directory: %s\nCommand: %s\n\n' \
        "$working_directory" \
        "$command_text" \
        >"$command_log"

    set +e
    (
        cd -- "$working_directory"
        bash -Eeuo pipefail -c "$command_text"
    ) 2>&1 | while IFS= read -r line || [[ -n "$line" ]]; do
        printf '%s\n' "$line" >>"$command_log"
        patch_command_line "$command_name" "$line"
    done
    exit_code="${PIPESTATUS[0]}"
    set -e

    if ((exit_code == 0)); then
        patch_success "Command '$command_name' completed successfully. Log: $command_log"
        return 0
    fi

    patch_error "Command '$command_name' failed with exit code $exit_code. Log: $command_log"
    return "$exit_code"
}

patch_prompt_yes_no()
{
    local prompt="$1"
    local default_answer="${2:-no}"
    local answer suffix

    if [[ "${PATCH_ASSUME_YES:-0}" == '1' ]]; then
        patch_info "$prompt [automatically answered yes]"
        return 0
    fi

    if [[ "${PATCH_NON_INTERACTIVE:-0}" == '1' || ! -r /dev/tty ]]; then
        patch_warning "$prompt [non-interactive mode: no]"
        return 1
    fi

    if [[ "$default_answer" == 'yes' ]]; then
        suffix='[Y/n]'
    else
        suffix='[y/N]'
    fi

    printf '%b[PATCH][PROMPT]%b %s %s ' \
        "${PATCH_COLOR_BOLD}${PATCH_COLOR_YELLOW}" \
        "$PATCH_COLOR_RESET" \
        "$prompt" \
        "$suffix" \
        >/dev/tty
    IFS= read -r answer </dev/tty

    case "${answer,,}" in
        y | yes | o | oui) return 0 ;;
        '') [[ "$default_answer" == 'yes' ]] ;;
        *) return 1 ;;
    esac
}

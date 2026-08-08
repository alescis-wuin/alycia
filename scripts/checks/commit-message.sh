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

# shellcheck disable=SC1090
source "$COMMON_SCRIPT"

readonly MESSAGE_FILE="${1:-}"

[[ -n "$MESSAGE_FILE" ]] ||
    die "Usage: $0 <commit-message-file>"

[[ -f "$MESSAGE_FILE" ]] ||
    die "Commit message file not found: $MESSAGE_FILE"

section "Commit message"

normalized_message="$(
    sed \
        -e '/^[[:space:]]*#/d' \
        -e 's/[[:space:]]\+$//' \
        "$MESSAGE_FILE"
)"

[[ -n "${normalized_message//[$'\n\r\t ']/}" ]] ||
    die "The commit message is empty."

if printf '%s\n' "$normalized_message" |
    grep -Eiq \
        '(^|[^[:alnum:]_])(AI|IA)([^[:alnum:]_]|$)|ChatGPT|OpenAI|Claude|Copilot|artificial intelligence|intelligence artificielle'
then
    die "Commit messages must not mention AI tools or artificial intelligence."
fi

header="$(
    printf '%s\n' "$normalized_message" |
        sed -n '1p'
)"

readonly header_pattern='^(feature|fix|chore|test|docs|refactor|perf|build|ci|style|release|hotfix)\(([a-z0-9][a-z0-9-]*)\): ([[:alnum:]].*)$'

[[ "$header" =~ $header_pattern ]] ||
    die "Invalid header. Expected: <type>(<scope>): <title>"

((${#header} <= 100)) ||
    die "Commit header exceeds 100 characters."

branch_name="$(
    git -C "$REPOSITORY_ROOT" branch --show-current
)"

[[ -n "$branch_name" ]] ||
    die "Detached HEAD is not supported for commit message validation."

branch_without_agent="${branch_name#agent/}"
expected_scope="${branch_without_agent#*/}"
actual_scope="${BASH_REMATCH[2]}"

[[ "$actual_scope" == "$expected_scope" ]] ||
    die "Commit scope '$actual_scope' must match branch name '$expected_scope'."

body="$(
    printf '%s\n' "$normalized_message" |
        awk '
            NR == 1 {
                next
            }

            !body_started && /^[[:space:]]*$/ {
                next
            }

            !body_started {
                body_started = 1
            }

            body_started {
                lines[++count] = $0
            }

            END {
                for (line_number = 1; line_number <= count; line_number++) {
                    print lines[line_number]
                }
            }
        '
)"

[[ -n "${body//[$'\n\r\t ']/}" ]] ||
    die "A descriptive body and footer are required."

if ! printf '%s\n' "$body" |
    grep -Eq '^[*-][[:space:]]+'
then
    die "The commit body must contain at least one bullet item."
fi

footer="$(
    printf '%s\n' "$body" |
        awk '
            /^[[:space:]]*$/ {
                paragraph = ""
                next
            }

            {
                paragraph = paragraph $0 "\n"
                last_paragraph = paragraph
            }

            END {
                printf "%s", last_paragraph
            }
        '
)"

printf '%s\n' "$footer" |
    grep -Eq \
        '^(Refs|Closes|Fixes|Issue|Ticket|BREAKING CHANGE):[[:space:]]+.+$' ||
    die "The final paragraph must contain a supported footer."

if printf '%s\n' "$normalized_message" |
    awk 'length($0) > 120 { exit 1 }'
then
    :
else
    die "A commit message line exceeds 120 characters."
fi

success "Commit message follows repository conventions."

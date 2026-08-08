#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIRECTORY="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly SCRIPT_DIRECTORY
REPOSITORY_ROOT="$(cd -- "$SCRIPT_DIRECTORY/../.." && pwd)"
readonly REPOSITORY_ROOT
readonly PATCH_TOOL="$SCRIPT_DIRECTORY/patch_tool.py"
readonly MANUAL_PROCESS_TOOL="$SCRIPT_DIRECTORY/manual_process.py"
readonly LOGGING_LIBRARY="$SCRIPT_DIRECTORY/lib/logging.sh"
readonly DOTNET_VERIFY="$REPOSITORY_ROOT/scripts/toolchain/verify-dotnet.sh"

# shellcheck disable=SC1090
source "$LOGGING_LIBRARY"

export PATCH_TOTAL_STEPS=15
export PATCH_CURRENT_STEP=0
export PATCH_CURRENT_STAGE='INIT'
PATCH_RESULT='failed'
PATCH_NAME_VALUE='unknown'
PATCH_COMMIT_SHA=''
PATCH_MANUAL_PROCESS_PID=''
PATCH_MANUAL_PROCESS_LOG=''
PATCH_MANUAL_PROCESS_PID_FILE=''
PATCH_MANUAL_PROCESS_STATUS_FILE=''
VALIDATION_FAILURES=0

archive_input="${PATCH:-}"
downloads_directory="${PATCH_DOWNLOADS_DIR:-$HOME/Téléchargements}"

if [[ -z "$archive_input" ]]; then
    printf 'Usage: make patch PATCH=<archive-name-without-.zip>\n' >&2
    exit 2
fi

if [[ "$archive_input" == */* ]]; then
    archive_path="$archive_input"
else
    archive_path="$downloads_directory/${archive_input%.zip}.zip"
fi
archive_path="$(realpath -m -- "$archive_path")"
archive_stem="$(basename -- "${archive_path%.zip}")"
log_timestamp="$(date '+%Y%m%d-%H%M%S')"
log_directory="$REPOSITORY_ROOT/logs/patches/$log_timestamp-$archive_stem"
patch_logging_init "$log_directory"

write_summary()
{
    local exit_code="$1"
    local finished_at

    finished_at="$(date --iso-8601=seconds)"
    python3 - \
        "$PATCH_LOG_DIRECTORY/summary.json" \
        "$PATCH_NAME_VALUE" \
        "$PATCH_RESULT" \
        "$exit_code" \
        "${CURRENT_BRANCH_VALUE:-unknown}" \
        "$PATCH_COMMIT_SHA" \
        "$finished_at" \
        "$PATCH_LOG_DIRECTORY" <<'PYTHON'
import json
import sys
from pathlib import Path

Path(sys.argv[1]).write_text(json.dumps({
    "patch": sys.argv[2],
    "result": sys.argv[3],
    "exit_code": int(sys.argv[4]),
    "branch": sys.argv[5],
    "commit_sha": sys.argv[6],
    "finished_at": sys.argv[7],
    "log_directory": sys.argv[8],
}, indent=2) + "\n", encoding="utf-8")
PYTHON
}

on_exit()
{
    local exit_code="$?"

    patch_stop_manual_process || true
    write_summary "$exit_code" || true
    if ((exit_code == 0)); then
        patch_success "Patch workflow completed. Logs: $PATCH_LOG_DIRECTORY"
    else
        patch_error "Patch workflow stopped. Repository state was preserved for diagnosis. Logs: $PATCH_LOG_DIRECTORY"
    fi
}
trap on_exit EXIT

require_command()
{
    command -v "$1" >/dev/null 2>&1 || {
        patch_error "Required command not found: $1"
        exit 1
    }
    patch_success "Required command available: $1"
}

manifest_get()
{
    python3 "$PATCH_TOOL" get --manifest "$manifest_path" --path "$1"
}

patch_manual_process_is_running()
{
    [[ -n "$PATCH_MANUAL_PROCESS_PID" ]] &&
        kill -0 "$PATCH_MANUAL_PROCESS_PID" 2>/dev/null
}

patch_manual_process_exit_code()
{
    [[ -f "$PATCH_MANUAL_PROCESS_STATUS_FILE" ]] ||
        return 1

    local exit_code
    exit_code="$(<"$PATCH_MANUAL_PROCESS_STATUS_FILE")"

    [[ "$exit_code" =~ ^[0-9]+$ ]] ||
        return 1

    printf '%s' "$exit_code"
}

patch_stop_manual_process()
{
    [[ -n "$PATCH_MANUAL_PROCESS_PID" ]] ||
        return 0

    if patch_manual_process_is_running; then
        patch_info "Stopping isolated manual process group $PATCH_MANUAL_PROCESS_PID."

        kill -TERM -- "-$PATCH_MANUAL_PROCESS_PID" 2>/dev/null || true

        for _ in {1..50}; do
            patch_manual_process_is_running ||
                break

            sleep 0.1
        done

        if patch_manual_process_is_running; then
            patch_warning "The manual process did not stop after SIGTERM; sending SIGKILL."
            kill -KILL -- "-$PATCH_MANUAL_PROCESS_PID" 2>/dev/null || true
        fi
    fi

    PATCH_MANUAL_PROCESS_PID=''
}

patch_start_manual_process()
{
    local command_text="$1"

    PATCH_MANUAL_PROCESS_LOG="$PATCH_COMMAND_LOG_DIRECTORY/$(printf '%02d' "$PATCH_CURRENT_STEP")-manual-test.log"
    PATCH_MANUAL_PROCESS_PID_FILE="$PATCH_SNAPSHOT_DIRECTORY/manual-test.pid"
    PATCH_MANUAL_PROCESS_STATUS_FILE="$PATCH_SNAPSHOT_DIRECTORY/manual-test.status"

    rm -f -- \
        "$PATCH_MANUAL_PROCESS_PID_FILE" \
        "$PATCH_MANUAL_PROCESS_STATUS_FILE"

    patch_info "Starting isolated manual command: $command_text"

    python3 "$MANUAL_PROCESS_TOOL" start \
        --cwd "$REPOSITORY_ROOT" \
        --command "$command_text" \
        --log-file "$PATCH_MANUAL_PROCESS_LOG" \
        --pid-file "$PATCH_MANUAL_PROCESS_PID_FILE" \
        --status-file "$PATCH_MANUAL_PROCESS_STATUS_FILE"

    PATCH_MANUAL_PROCESS_PID="$(<"$PATCH_MANUAL_PROCESS_PID_FILE")"

    [[ "$PATCH_MANUAL_PROCESS_PID" =~ ^[0-9]+$ ]] || {
        patch_error "Invalid isolated manual process identifier."
        return 1
    }

    sleep 0.2

    if [[ -f "$PATCH_MANUAL_PROCESS_STATUS_FILE" ]]; then
        local early_exit_code
        early_exit_code="$(patch_manual_process_exit_code)" || {
            patch_error "Invalid manual process status file."
            return 1
        }

        if ((early_exit_code != 0)); then
            patch_error "The manual command exited early with code $early_exit_code. Log: $PATCH_MANUAL_PROCESS_LOG"
            return "$early_exit_code"
        fi
    fi

    patch_success "Manual command started in isolated session $PATCH_MANUAL_PROCESS_PID. Log: $PATCH_MANUAL_PROCESS_LOG"
}

patch_step PREREQUISITES "Checking patch workflow prerequisites."
for command_name in bash git make python3 unzip sha256sum realpath; do
    require_command "$command_name"
done
[[ -x "$DOTNET_VERIFY" ]] || {
    patch_error "Required .NET toolchain verifier not found: $DOTNET_VERIFY"
    exit 1
}
if ! "$DOTNET_VERIFY"; then
    patch_error "No compatible repository .NET SDK is available."
    patch_error "Run make toolchain-bootstrap before applying patches."
    exit 1
fi
patch_success "Repository .NET toolchain verified: $DOTNET_VERIFY"
[[ -f "$MANUAL_PROCESS_TOOL" ]] || {
    patch_error "Required manual process helper not found: $MANUAL_PROCESS_TOOL"
    exit 1
}
patch_success "Manual process helper available: $MANUAL_PROCESS_TOOL"
[[ -f "$archive_path" ]] || {
    patch_error "Patch archive not found: $archive_path"
    exit 1
}
[[ "$archive_path" == *.zip ]] || {
    patch_error "Patch archive must use the .zip extension: $archive_path"
    exit 1
}
patch_success "Patch archive located: $archive_path"

patch_step EXTRACT "Testing, safely inspecting, and extracting the patch archive."
patch_run_command unzip-test "unzip -t '$archive_path'" "$REPOSITORY_ROOT"
package_directory="$(python3 "$PATCH_TOOL" safe-extract \
    --archive "$archive_path" \
    --destination "$PATCH_LOG_DIRECTORY/work")"
readonly package_directory
manifest_path="$package_directory/manifest.json"
readonly manifest_path
patch_success "Archive extracted into the isolated log workspace: $package_directory"

patch_step PACKAGE "Validating manifest structure, declared files, extensions, content, and checksums."
patch_run_command package-structure \
    "python3 '$PATCH_TOOL' validate-dir --package-dir '$package_directory'" \
    "$REPOSITORY_ROOT"
patch_run_command sha256sum \
    "sha256sum -c SHA256SUMS" \
    "$package_directory"
cp -- "$manifest_path" "$PATCH_SNAPSHOT_DIRECTORY/manifest.json"
PATCH_NAME_VALUE="$(manifest_get patch.name)"
patch_description="$(manifest_get patch.description)"
[[ "$(basename -- "$package_directory")" == "$PATCH_NAME_VALUE" ]] || {
    patch_error "Extracted top-level directory must match patch.name: $PATCH_NAME_VALUE"
    exit 1
}
[[ "$archive_stem" == "$PATCH_NAME_VALUE" ]] || {
    patch_error "Archive filename must match patch.name: expected $PATCH_NAME_VALUE.zip"
    exit 1
}
patch_success "Validated patch package: $PATCH_NAME_VALUE"
patch_info "Description: $patch_description"

patch_step REPOSITORY "Verifying repository identity, starting branch, required paths, and expected pre-patch status."
actual_root="$(git -C "$REPOSITORY_ROOT" rev-parse --show-toplevel)"
[[ "$actual_root" == "$REPOSITORY_ROOT" ]] || {
    patch_error "Repository root mismatch: $actual_root"
    exit 1
}
[[ "$(basename -- "$REPOSITORY_ROOT")" == "$(manifest_get repository.root_name)" ]] || {
    patch_error "Repository directory name does not match the manifest."
    exit 1
}
start_branch="$(manifest_get repository.start_branch)"
end_branch="$(manifest_get repository.end_branch)"
CURRENT_BRANCH_VALUE="$(git -C "$REPOSITORY_ROOT" branch --show-current)"
export CURRENT_BRANCH_VALUE
[[ "$CURRENT_BRANCH_VALUE" == "$start_branch" ]] || {
    patch_error "Expected starting branch '$start_branch', found '$CURRENT_BRANCH_VALUE'."
    exit 1
}
while IFS= read -r required_path; do
    [[ -e "$REPOSITORY_ROOT/$required_path" ]] || {
        patch_error "Required repository path is missing: $required_path"
        exit 1
    }
done < <(python3 "$PATCH_TOOL" list --manifest "$manifest_path" --path preconditions.required_paths)
pre_status_file="$PATCH_SNAPSHOT_DIRECTORY/pre-status.txt"
git -C "$REPOSITORY_ROOT" status --short --untracked-files=all >"$pre_status_file"
python3 "$PATCH_TOOL" validate-status \
    --manifest "$manifest_path" \
    --section preconditions \
    --status-file "$pre_status_file"
patch_success "Starting branch and pre-patch repository state match the manifest."

patch_step PRE_INSPECTION "Inspecting repository changes before applying the patch."
patch_run_command pre-diff-check "git diff --check" "$REPOSITORY_ROOT"
patch_run_command pre-status "git status --short --untracked-files=all" "$REPOSITORY_ROOT"
patch_run_command pre-diff-stat "git --no-pager diff --stat" "$REPOSITORY_ROOT"

patch_step BRANCH "Preparing the manifest target branch."
create_branch="$(manifest_get repository.branch.create)"
existing_policy="$(manifest_get repository.branch.existing_policy)"
if [[ "$create_branch" == 'true' ]]; then
    if git -C "$REPOSITORY_ROOT" show-ref --verify --quiet "refs/heads/$end_branch"; then
        if [[ "$existing_policy" == 'require' ]]; then
            git -C "$REPOSITORY_ROOT" switch "$end_branch"
        else
            patch_error "Target branch already exists: $end_branch"
            exit 1
        fi
    else
        git -C "$REPOSITORY_ROOT" switch -c "$end_branch"
    fi
else
    [[ "$CURRENT_BRANCH_VALUE" == "$end_branch" ]] || {
        patch_error "Manifest requires existing branch '$end_branch'."
        exit 1
    }
fi
CURRENT_BRANCH_VALUE="$(git -C "$REPOSITORY_ROOT" branch --show-current)"
export CURRENT_BRANCH_VALUE
[[ "$CURRENT_BRANCH_VALUE" == "$end_branch" ]] || {
    patch_error "Target branch activation failed: $CURRENT_BRANCH_VALUE"
    exit 1
}
patch_success "Active target branch: $CURRENT_BRANCH_VALUE"

patch_step APPLY "Applying the validated package entrypoint."
entrypoint="$(manifest_get apply.entrypoint)"
chmod u+x -- "$package_directory/$entrypoint"
patch_run_command apply \
    "'$package_directory/$entrypoint' '$REPOSITORY_ROOT'" \
    "$REPOSITORY_ROOT"

patch_step POST_INSPECTION "Inspecting and validating repository changes after patch application."
patch_run_command post-diff-check "git diff --check" "$REPOSITORY_ROOT"
patch_run_command post-status "git status --short --untracked-files=all" "$REPOSITORY_ROOT"
patch_run_command post-diff-stat "git --no-pager diff --stat" "$REPOSITORY_ROOT"
post_status_file="$PATCH_SNAPSHOT_DIRECTORY/post-status.txt"
git -C "$REPOSITORY_ROOT" status --short --untracked-files=all >"$post_status_file"
python3 "$PATCH_TOOL" validate-status \
    --manifest "$manifest_path" \
    --section postconditions \
    --status-file "$post_status_file"
python3 "$PATCH_TOOL" validate-paths \
    --manifest "$manifest_path" \
    --status-file "$post_status_file"
patch_success "Post-patch status, modification count, files, and directories match the manifest."

patch_step VALIDATE "Running every configured validation command; failures are aggregated."
VALIDATION_FAILURES=0
while IFS=$'\t' read -r validation_name validation_command; do
    if patch_run_command "$validation_name" "$validation_command" "$REPOSITORY_ROOT"; then
        :
    else
        VALIDATION_FAILURES="$((VALIDATION_FAILURES + 1))"
    fi
done < <(
    python3 "$PATCH_TOOL" rows \
        --manifest "$manifest_path" \
        --path validation.commands \
        --fields name,command
)
if ((VALIDATION_FAILURES > 0)); then
    patch_error "$VALIDATION_FAILURES validation command(s) failed. All configured validations were executed."
    exit 1
fi
patch_success "All configured validation commands succeeded."

patch_step MANUAL_TEST "Evaluating whether an interactive application test is required."
manual_mode="$(manifest_get manual_test.mode)"
if [[ "$manual_mode" != 'never' ]]; then
    manual_prompt="$(manifest_get manual_test.prompt)"
    if patch_prompt_yes_no "$manual_prompt" no; then
        manual_command="$(manifest_get manual_test.command)"
        patch_start_manual_process "$manual_command"

        acceptance_prompt="$(manifest_get manual_test.acceptance_prompt)"
        if ! patch_prompt_yes_no "$acceptance_prompt" no; then
            patch_stop_manual_process
            patch_error "The manual application test was rejected."
            exit 1
        fi

        if [[ -f "$PATCH_MANUAL_PROCESS_STATUS_FILE" ]]; then
            manual_exit_code="$(patch_manual_process_exit_code)" || {
                patch_stop_manual_process
                patch_error "Invalid manual process status file."
                exit 1
            }

            if ((manual_exit_code != 0)); then
                patch_stop_manual_process
                patch_error "The manual command failed with code $manual_exit_code. Log: $PATCH_MANUAL_PROCESS_LOG"
                exit "$manual_exit_code"
            fi
        elif ! patch_manual_process_is_running; then
            patch_stop_manual_process
            patch_error "The manual command ended without a status result. Log: $PATCH_MANUAL_PROCESS_LOG"
            exit 1
        fi

        patch_stop_manual_process
        patch_success "The manual application test was accepted."
    elif [[ "$manual_mode" == 'required' ]]; then
        patch_error "The manifest requires a manual application test before committing."
        exit 1
    else
        patch_warning "Optional manual application test skipped."
    fi
else
    patch_info "The manifest does not require an application test."
fi

patch_step STAGE "Staging only manifest-authorized repository paths."
mapfile -t stage_paths < <(
    python3 "$PATCH_TOOL" list --manifest "$manifest_path" --path stage.paths
)
git -C "$REPOSITORY_ROOT" add -- "${stage_paths[@]}"
staged_paths_file="$PATCH_SNAPSHOT_DIRECTORY/staged-paths.txt"
git -C "$REPOSITORY_ROOT" diff --cached --name-only >"$staged_paths_file"
printf '%s\n' "${stage_paths[@]}" | sort -u >"$PATCH_SNAPSHOT_DIRECTORY/expected-staged-paths.txt"
sort -u "$staged_paths_file" >"$PATCH_SNAPSHOT_DIRECTORY/actual-staged-paths.txt"
if ! diff -u \
    "$PATCH_SNAPSHOT_DIRECTORY/expected-staged-paths.txt" \
    "$PATCH_SNAPSHOT_DIRECTORY/actual-staged-paths.txt" \
    >"$PATCH_COMMAND_LOG_DIRECTORY/staged-path-comparison.log"
then
    patch_error "Staged paths differ from the manifest. See staged-path-comparison.log."
    exit 1
fi
patch_run_command staged-diff-check "git diff --cached --check" "$REPOSITORY_ROOT"
patch_run_command staged-diff-stat "git --no-pager diff --cached --stat" "$REPOSITORY_ROOT"
patch_run_command staged-status "git status --short --untracked-files=all" "$REPOSITORY_ROOT"
patch_run_command staged-safety "make --no-print-directory staged" "$REPOSITORY_ROOT"

patch_step COMMIT "Generating the manifest-defined commit message and creating the signed commit."
commit_enabled="$(manifest_get commit.enabled)"
if [[ "$commit_enabled" == 'true' ]]; then
    commit_message_file="$PATCH_SNAPSHOT_DIRECTORY/commit-message.txt"
    python3 "$PATCH_TOOL" render-commit \
        --manifest "$manifest_path" \
        --output "$commit_message_file"
    commit_mode="$(manifest_get commit.mode)"
    if [[ "$commit_mode" == 'prompt' ]] && ! patch_prompt_yes_no "Create the signed commit now?" no; then
        patch_error "Commit creation declined. Validated changes remain staged."
        exit 1
    fi
    commit_sign="$(manifest_get commit.sign)"
    if [[ "$commit_sign" == 'true' ]]; then
        patch_run_command commit "git commit -S -F '$commit_message_file'" "$REPOSITORY_ROOT"
    else
        patch_run_command commit "git commit -F '$commit_message_file'" "$REPOSITORY_ROOT"
    fi
    PATCH_COMMIT_SHA="$(git -C "$REPOSITORY_ROOT" rev-parse HEAD)"
    export PATCH_COMMIT_SHA
    patch_run_command verify-commit "git verify-commit HEAD" "$REPOSITORY_ROOT"
else
    patch_warning "Commit creation is disabled by the manifest."
fi

patch_step POST_COMMIT "Running all configured post-commit validations."
VALIDATION_FAILURES=0
while IFS=$'\t' read -r validation_name validation_command; do
    if patch_run_command "$validation_name" "$validation_command" "$REPOSITORY_ROOT"; then
        :
    else
        VALIDATION_FAILURES="$((VALIDATION_FAILURES + 1))"
    fi
done < <(
    python3 "$PATCH_TOOL" rows \
        --manifest "$manifest_path" \
        --path post_commit.commands \
        --fields name,command
)
if ((VALIDATION_FAILURES > 0)); then
    patch_error "$VALIDATION_FAILURES post-commit validation command(s) failed."
    exit 1
fi
patch_success "All post-commit validation commands succeeded."

patch_step FINAL_STATE "Verifying final branch, worktree, and commit state."
CURRENT_BRANCH_VALUE="$(git -C "$REPOSITORY_ROOT" branch --show-current)"
export CURRENT_BRANCH_VALUE
[[ "$CURRENT_BRANCH_VALUE" == "$end_branch" ]] || {
    patch_error "Final branch mismatch: expected '$end_branch', found '$CURRENT_BRANCH_VALUE'."
    exit 1
}
if [[ "$commit_enabled" == 'true' && -n "$(git -C "$REPOSITORY_ROOT" status --short)" ]]; then
    patch_error "The worktree is not clean after commit creation."
    exit 1
fi
patch_run_command final-log \
    "git --no-pager log --show-signature --format=fuller -1" \
    "$REPOSITORY_ROOT"
patch_success "Final repository state is valid."

patch_step SUMMARY "Writing structured workflow summary."
PATCH_RESULT='success'
patch_success "Patch '$PATCH_NAME_VALUE' completed successfully on branch '$CURRENT_BRANCH_VALUE'."
patch_info "Commit: ${PATCH_COMMIT_SHA:-not-created}"
patch_info "Console log: $PATCH_CONSOLE_LOG"
patch_info "Structured events: $PATCH_EVENT_LOG"
patch_info "Command logs: $PATCH_COMMAND_LOG_DIRECTORY"

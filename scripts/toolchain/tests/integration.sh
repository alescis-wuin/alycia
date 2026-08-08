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

passed=0
failed=0

pass()
{
    printf '[OK] %s\n' "$1"
    passed=$((passed + 1))
}

fail()
{
    printf '[ERROR] %s\n' "$1" >&2
    failed=$((failed + 1))
}

assert_contains()
{
    local path="$1"
    local text="$2"
    local description="$3"

    if grep -Fq -- "$text" "$path"; then
        pass "$description"
    else
        fail "$description"
    fi
}

printf '\n=== Repository .NET toolchain integration tests ===\n'

if python3 - "$REPOSITORY_ROOT/global.json" <<'PYTHON'
import json
import pathlib
import sys

value = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
sdk = value.get("sdk", {})

assert sdk.get("paths") == [".dotnet", "$host$"]
message = sdk.get("errorMessage")
assert isinstance(message, str) and "toolchain-bootstrap" in message
PYTHON
then
    pass "global.json prioritizes the local SDK and provides recovery guidance."
else
    fail "global.json prioritizes the local SDK and provides recovery guidance."
fi

for ignored_path in \
    '.dotnet/' \
    '.dotnet.bootstrap.lock/' \
    '.toolchain-cache/' \
    '.support/'; do
    if grep -Fxq -- "$ignored_path" "$REPOSITORY_ROOT/.gitignore"; then
        pass ".gitignore excludes $ignored_path"
    else
        fail ".gitignore excludes $ignored_path"
    fi
done

assert_contains \
    "$REPOSITORY_ROOT/Makefile" \
    "DOTNET := \$(ROOT)/scripts/toolchain/dotnet.sh" \
    "The Makefile owns the stable dotnet wrapper path."

for target in \
    toolchain-bootstrap \
    toolchain-check \
    toolchain-info \
    toolchain-clean \
    toolchain-self-test; do
    if make -C "$REPOSITORY_ROOT" -n "$target" >/dev/null; then
        pass "Make target is available: $target"
    else
        fail "Make target is available: $target"
    fi
done

if grep -Eq '^[[:space:]]+dotnet[[:space:]]' "$REPOSITORY_ROOT/Makefile"; then
    fail "The Makefile contains no direct dotnet recipe invocation."
else
    pass "The Makefile contains no direct dotnet recipe invocation."
fi

if grep -Eq '^[[:space:]]*dotnet[[:space:]]' \
    "$REPOSITORY_ROOT/scripts/checks/vulnerabilities.sh"; then
    fail "The vulnerability audit uses the repository wrapper."
else
    pass "The vulnerability audit uses the repository wrapper."
fi

assert_contains \
    "$REPOSITORY_ROOT/scripts/checks/vulnerabilities.sh" \
    'scripts/toolchain/dotnet.sh' \
    "The vulnerability audit declares the wrapper path."

if grep -Fq -- \
    'for command_name in bash git make dotnet' \
    "$REPOSITORY_ROOT/scripts/patch/apply-package.sh"; then
    fail "The patch runner does not require a system dotnet command."
else
    pass "The patch runner does not require a system dotnet command."
fi

assert_contains \
    "$REPOSITORY_ROOT/scripts/patch/apply-package.sh" \
    'scripts/toolchain/verify-dotnet.sh' \
    "The patch runner verifies the repository SDK before applying packages."

printf '\nToolchain integration tests: %d passed, %d failed.\n' \
    "$passed" \
    "$failed"

((failed == 0))

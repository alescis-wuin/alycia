#!/usr/bin/env bash

ALICIA_TOOLCHAIN_LIBRARY_DIR="$(
    cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &&
        pwd
)"
readonly ALICIA_TOOLCHAIN_LIBRARY_DIR

ALICIA_REPOSITORY_ROOT="$(
    cd -- "$ALICIA_TOOLCHAIN_LIBRARY_DIR/../.." &&
        pwd
)"
readonly ALICIA_REPOSITORY_ROOT

alicia_toolchain_error()
{
    printf '[ERROR] %s\n' "$*" >&2
}

alicia_toolchain_info()
{
    printf '[INFO] %s\n' "$*"
}

alicia_toolchain_success()
{
    printf '[OK] %s\n' "$*"
}

alicia_required_sdk_version()
{
    python3 - "$ALICIA_REPOSITORY_ROOT/global.json" <<'PYTHON'
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
value = json.loads(path.read_text(encoding="utf-8"))
version = value.get("sdk", {}).get("version")

if not isinstance(version, str) or not version.strip():
    raise SystemExit("global.json must define sdk.version")

print(version.strip())
PYTHON
}

alicia_sdk_roll_forward_policy()
{
    python3 - "$ALICIA_REPOSITORY_ROOT/global.json" <<'PYTHON'
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
value = json.loads(path.read_text(encoding="utf-8"))
policy = value.get("sdk", {}).get("rollForward", "patch")

if not isinstance(policy, str) or not policy.strip():
    raise SystemExit("global.json sdk.rollForward must be a string")

print(policy.strip())
PYTHON
}

alicia_sdk_version_is_compatible()
{
    local required_version="$1"
    local candidate_version="$2"
    local policy="$3"

    python3 - \
        "$required_version" \
        "$candidate_version" \
        "$policy" <<'PYTHON'
import re
import sys

required_text, candidate_text, policy = sys.argv[1:]
pattern = re.compile(r"^(\d+)\.(\d+)\.(\d+)$")
required_match = pattern.fullmatch(required_text)
candidate_match = pattern.fullmatch(candidate_text)

if required_match is None or candidate_match is None:
    raise SystemExit(1)

required = tuple(int(value) for value in required_match.groups())
candidate = tuple(int(value) for value in candidate_match.groups())
required_band = required[2] // 100
candidate_band = candidate[2] // 100

if policy == "disable":
    compatible = candidate == required
elif policy in {"patch", "latestPatch"}:
    compatible = (
        candidate[0:2] == required[0:2]
        and candidate_band == required_band
        and candidate >= required
    )
elif policy in {"feature", "latestFeature"}:
    compatible = candidate[0:2] == required[0:2] and candidate >= required
elif policy in {"minor", "latestMinor"}:
    compatible = candidate[0] == required[0] and candidate >= required
elif policy in {"major", "latestMajor"}:
    compatible = candidate >= required
else:
    raise SystemExit(2)

raise SystemExit(0 if compatible else 1)
PYTHON
}

alicia_dotnet_version()
{
    local candidate="$1"
    local candidate_root="${2:-}"

    if [[ -n "$candidate_root" ]]; then
        (
            cd -- "$ALICIA_REPOSITORY_ROOT" ||
                return 1

            DOTNET_ROOT="$candidate_root" \
            DOTNET_MULTILEVEL_LOOKUP=0 \
                "$candidate" --version
        )

        return
    fi

    (
        cd -- "$ALICIA_REPOSITORY_ROOT" ||
            return 1

        "$candidate" --version
    )
}

alicia_is_repository_local_dotnet()
{
    local candidate="$1"
    local local_dotnet="$ALICIA_REPOSITORY_ROOT/.dotnet/dotnet"

    [[ "$(realpath -m -- "$candidate")" == \
        "$(realpath -m -- "$local_dotnet")" ]]
}

alicia_detect_dotnet_os()
{
    case "$(uname -s)" in
        Linux)
            printf 'linux\n'
            ;;
        Darwin)
            printf 'osx\n'
            ;;
        *)
            alicia_toolchain_error \
                "Unsupported operating system: $(uname -s)"
            return 1
            ;;
    esac
}

alicia_detect_dotnet_architecture()
{
    case "$(uname -m)" in
        x86_64 | amd64)
            printf 'x64\n'
            ;;
        aarch64 | arm64)
            printf 'arm64\n'
            ;;
        armv7l | armv8l)
            printf 'arm\n'
            ;;
        s390x)
            printf 's390x\n'
            ;;
        ppc64le)
            printf 'ppc64le\n'
            ;;
        riscv64)
            printf 'riscv64\n'
            ;;
        *)
            alicia_toolchain_error \
                "Unsupported architecture: $(uname -m)"
            return 1
            ;;
    esac
}

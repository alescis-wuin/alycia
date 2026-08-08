#!/usr/bin/env bash
set -Eeuo pipefail

TEST_SCRIPT_DIR="$(
    cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &&
        pwd
)"
readonly TEST_SCRIPT_DIR

SOURCE_TOOLCHAIN_DIR="$(
    cd -- "$TEST_SCRIPT_DIR/.." &&
        pwd
)"
readonly SOURCE_TOOLCHAIN_DIR

passed=0
failed=0

report_success()
{
    printf '[OK] %s\n' "$1"
    passed=$((passed + 1))
}

report_failure()
{
    printf '[ERROR] %s\n' "$1" >&2
    failed=$((failed + 1))
}

run_test()
{
    local name="$1"
    local function_name="$2"
    local temporary
    temporary="$(mktemp -d)"

    if TEST_TEMPORARY="$temporary" "$function_name"; then
        report_success "$name"
    else
        report_failure "$name"
    fi

    rm -rf -- "$temporary"
}

create_repository()
{
    local repository="$1"
    local version="${2:-10.0.110}"

    mkdir -p -- "$repository/scripts"
    cp -R -- "$SOURCE_TOOLCHAIN_DIR" "$repository/scripts/toolchain"

    cat >"$repository/global.json" <<EOF
{
  "sdk": {
    "rollForward": "latestPatch",
    "version": "$version"
  }
}
EOF
}

create_fake_dotnet()
{
    local path="$1"
    local version="$2"

    mkdir -p -- "$(dirname -- "$path")"

    cat >"$path" <<EOF
#!/usr/bin/env bash
set -Eeuo pipefail

if [[ "\${1:-}" == '--version' ]]; then
    printf '%s\\n' '$version'
    exit 0
fi

printf 'DOTNET_ROOT=%s\\n' "\${DOTNET_ROOT:-}"
printf 'ARG=%s\\n' "\$@"
EOF

    chmod 0755 -- "$path"
}

test_local_precedes_system()
{
    local temporary="$TEST_TEMPORARY"

    local repository="$temporary/repository"
    local fake_bin="$temporary/bin"
    create_repository "$repository"
    create_fake_dotnet "$repository/.dotnet/dotnet" '10.0.115'
    create_fake_dotnet "$fake_bin/dotnet" '10.0.119'

    local source
    source="$(
        PATH="$fake_bin:$PATH" \
            "$repository/scripts/toolchain/resolve-dotnet.sh" \
                --source
    )"

    [[ "$source" == 'repository-local' ]]
}

test_system_fallback_accepts_latest_patch()
{
    local temporary="$TEST_TEMPORARY"

    local repository="$temporary/repository"
    local fake_bin="$temporary/bin"
    create_repository "$repository"
    create_fake_dotnet "$fake_bin/dotnet" '10.0.118'

    local version
    version="$(
        PATH="$fake_bin:$PATH" \
            "$repository/scripts/toolchain/resolve-dotnet.sh" \
                --version
    )"

    [[ "$version" == '10.0.118' ]]
}

test_wrong_feature_band_is_rejected()
{
    local temporary="$TEST_TEMPORARY"

    local repository="$temporary/repository"
    create_repository "$repository"
    create_fake_dotnet "$repository/.dotnet/dotnet" '10.0.200'

    ! ALICIA_DOTNET_DISABLE_SYSTEM=1 \
        "$repository/scripts/toolchain/resolve-dotnet.sh" \
            --path \
            >/dev/null 2>&1
}

test_wrapper_forwards_arguments_and_local_root()
{
    local temporary="$TEST_TEMPORARY"

    local repository="$temporary/repository"
    create_repository "$repository"
    create_fake_dotnet "$repository/.dotnet/dotnet" '10.0.110'

    local output
    output="$(
        ALICIA_DOTNET_DISABLE_SYSTEM=1 \
            "$repository/scripts/toolchain/dotnet.sh" \
                build \
                --configuration \
                Debug
    )"

    grep -Fq \
        "DOTNET_ROOT=$repository/.dotnet" \
        <<<"$output" &&
        grep -Fq 'ARG=build' <<<"$output" &&
        grep -Fq 'ARG=--configuration' <<<"$output" &&
        grep -Fq 'ARG=Debug' <<<"$output"
}

test_offline_bootstrap_installs_verified_archive()
{
    local temporary="$TEST_TEMPORARY"

    local repository="$temporary/repository"
    local archive_root="$temporary/archive-root"
    local archive="$temporary/sdk.tar.gz"
    local install="$repository/.dotnet"

    create_repository "$repository"
    create_fake_dotnet "$archive_root/dotnet" '10.0.110'
    tar -C "$archive_root" -czf "$archive" .
    local checksum
    checksum="$(sha256sum -- "$archive" | awk '{ print $1 }')"

    ALICIA_DOTNET_DISABLE_SYSTEM=1 \
        "$repository/scripts/toolchain/bootstrap-dotnet.sh" \
            --archive "$archive" \
            --sha256 "$checksum" \
            --offline-only

    [[ -x "$install/dotnet" ]] &&
        [[ "$("$install/dotnet" --version)" == '10.0.110' ]]
}

test_checksum_mismatch_preserves_existing_install()
{
    local temporary="$TEST_TEMPORARY"

    local repository="$temporary/repository"
    local archive_root="$temporary/archive-root"
    local archive="$temporary/sdk.tar.gz"

    create_repository "$repository"
    create_fake_dotnet "$repository/.dotnet/dotnet" '10.0.111'
    create_fake_dotnet "$archive_root/dotnet" '10.0.110'
    tar -C "$archive_root" -czf "$archive" .

    ! ALICIA_DOTNET_DISABLE_SYSTEM=1 \
        "$repository/scripts/toolchain/bootstrap-dotnet.sh" \
            --archive "$archive" \
            --sha256 "$(printf '0%.0s' {1..64})" \
            --offline-only \
            --force \
            >/dev/null 2>&1 &&
        [[ "$("$repository/.dotnet/dotnet" --version)" == '10.0.111' ]]
}

test_safe_extraction_rejects_traversal()
{
    local temporary="$TEST_TEMPORARY"

    local archive="$temporary/malicious.tar.gz"
    local destination="$temporary/destination"

    python3 - "$archive" <<'PYTHON'
import io
import pathlib
import tarfile
import sys

archive = pathlib.Path(sys.argv[1])
with tarfile.open(archive, "w:gz") as bundle:
    data = b"escape\n"
    member = tarfile.TarInfo("../escape")
    member.size = len(data)
    bundle.addfile(member, io.BytesIO(data))
PYTHON

    ! python3 \
        "$SOURCE_TOOLCHAIN_DIR/safe_extract_archive.py" \
        "$archive" \
        "$destination" \
        >/dev/null 2>&1 &&
        [[ ! -e "$temporary/escape" ]]
}

test_online_bootstrap_requires_verified_installer_flow()
{
    local temporary="$TEST_TEMPORARY"

    local repository="$temporary/repository"
    local fake_bin="$temporary/bin"
    local downloads="$temporary/downloads"

    create_repository "$repository"
    mkdir -p -- "$fake_bin" "$downloads"

    cat >"$downloads/dotnet-install.sh" <<'INSTALLER'
#!/usr/bin/env bash
set -Eeuo pipefail
install_directory=''
version=''
while (($# > 0)); do
    case "$1" in
        --install-dir)
            shift
            install_directory="$1"
            ;;
        --version)
            shift
            version="$1"
            ;;
    esac
    shift
done
mkdir -p -- "$install_directory"
cat >"$install_directory/dotnet" <<EOF
#!/usr/bin/env bash
if [[ "\${1:-}" == '--version' ]]; then
    printf '%s\\n' '$version'
    exit 0
fi
printf 'ARG=%s\\n' "\$@"
EOF
chmod 0755 -- "$install_directory/dotnet"
INSTALLER
    chmod 0755 -- "$downloads/dotnet-install.sh"
    printf 'signature\n' >"$downloads/dotnet-install.sig"
    printf 'key\n' >"$downloads/dotnet-install.asc"

    cat >"$fake_bin/curl" <<'CURL'
#!/usr/bin/env bash
set -Eeuo pipefail
destination=''
url=''
while (($# > 0)); do
    case "$1" in
        --output)
            shift
            destination="$1"
            ;;
        http*)
            url="$1"
            ;;
    esac
    shift
done
case "$url" in
    *dotnet-install.sh)
        source_file="$FAKE_DOWNLOAD_ROOT/dotnet-install.sh"
        ;;
    *dotnet-install.sig)
        source_file="$FAKE_DOWNLOAD_ROOT/dotnet-install.sig"
        ;;
    *dotnet-install.asc)
        source_file="$FAKE_DOWNLOAD_ROOT/dotnet-install.asc"
        ;;
    *)
        exit 3
        ;;
esac
cp -- "$source_file" "$destination"
CURL
    chmod 0755 -- "$fake_bin/curl"

    cat >"$fake_bin/gpg" <<'GPG'
#!/usr/bin/env bash
set -Eeuo pipefail
case " $* " in
    *' --fingerprint '*)
        printf 'fpr:::::::::2B930AB1228D11D5D7F6B6ACB9CF1A51FC7D3ACF:\n'
        ;;
    *)
        :
        ;;
esac
GPG
    chmod 0755 -- "$fake_bin/gpg"

    PATH="$fake_bin:$PATH" \
    FAKE_DOWNLOAD_ROOT="$downloads" \
    ALICIA_DOTNET_DISABLE_SYSTEM=1 \
    DOTNET_INSTALL_SCRIPT_URL='https://example.test/dotnet-install.sh' \
    DOTNET_INSTALL_SIGNATURE_URL='https://example.test/dotnet-install.sig' \
    DOTNET_INSTALL_KEY_URL='https://example.test/dotnet-install.asc' \
        "$repository/scripts/toolchain/bootstrap-dotnet.sh"

    [[ "$("$repository/.dotnet/dotnet" --version)" == '10.0.110' ]]
}

test_json_resolution_is_structured()
{
    local temporary="$TEST_TEMPORARY"

    local repository="$temporary/repository"
    create_repository "$repository"
    create_fake_dotnet "$repository/.dotnet/dotnet" '10.0.110'

    ALICIA_DOTNET_DISABLE_SYSTEM=1 \
        "$repository/scripts/toolchain/resolve-dotnet.sh" \
            --json |
        python3 -c '
import json
import sys
value = json.load(sys.stdin)
assert value["source"] == "repository-local"
assert value["version"] == "10.0.110"
assert value["required_version"] == "10.0.110"
assert value["roll_forward"] == "latestPatch"
'
}

printf '\n=== Repository .NET toolchain tests ===\n'

run_test \
    'Repository-local SDK precedes a system SDK.' \
    test_local_precedes_system
run_test \
    'System fallback accepts a compatible latest patch.' \
    test_system_fallback_accepts_latest_patch
run_test \
    'A different SDK feature band is rejected.' \
    test_wrong_feature_band_is_rejected
run_test \
    'The wrapper forwards arguments and sets DOTNET_ROOT locally.' \
    test_wrapper_forwards_arguments_and_local_root
run_test \
    'Verified offline SDK archives install successfully.' \
    test_offline_bootstrap_installs_verified_archive
run_test \
    'Checksum failure preserves an existing local installation.' \
    test_checksum_mismatch_preserves_existing_install
run_test \
    'Archive traversal is rejected safely.' \
    test_safe_extraction_rejects_traversal
run_test \
    'Online bootstrap follows the signed-installer verification path.' \
    test_online_bootstrap_requires_verified_installer_flow
run_test \
    'Resolver JSON output is structured and complete.' \
    test_json_resolution_is_structured

printf '\nToolchain tests: %d passed, %d failed.\n' \
    "$passed" \
    "$failed"

((failed == 0))

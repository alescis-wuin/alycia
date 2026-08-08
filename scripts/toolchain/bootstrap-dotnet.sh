#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(
    cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &&
        pwd
)"
readonly SCRIPT_DIR

# shellcheck source=scripts/toolchain/lib.sh
source "$SCRIPT_DIR/lib.sh"

install_directory="$ALICIA_REPOSITORY_ROOT/.dotnet"
cache_directory="$ALICIA_REPOSITORY_ROOT/.toolchain-cache"
offline_archive=''
offline_sha256=''
offline_only=0
force=0

readonly DEFAULT_INSTALLER_URL='https://dot.net/v1/dotnet-install.sh'
readonly DEFAULT_SIGNATURE_URL='https://dot.net/v1/dotnet-install.sig'
readonly DEFAULT_KEY_URL='https://dot.net/v1/dotnet-install.asc'
readonly INSTALLER_FINGERPRINT='2B930AB1228D11D5D7F6B6ACB9CF1A51FC7D3ACF'

usage()
{
    cat <<'EOF'
Usage: bootstrap-dotnet.sh [OPTIONS]

Options:
  --archive FILE       Install from a local .tar archive.
  --sha256 HASH        Required SHA-256 for --archive.
  --offline-only       Refuse network installation.
  --install-dir DIR    Override the repository-local installation directory.
  --cache-dir DIR      Override the offline archive cache directory.
  --force              Replace an existing local installation.
  --help               Show this help.

Without --archive, the script uses:
  .toolchain-cache/dotnet-sdk-<version>-<os>-<architecture>.tar.gz
when that file exists. Otherwise, it downloads and verifies Microsoft's signed
dotnet-install.sh before installing the pinned global.json SDK version.
EOF
}

while (($# > 0)); do
    case "$1" in
        --archive)
            shift
            (($# > 0)) || {
                alicia_toolchain_error '--archive requires a value.'
                exit 2
            }
            offline_archive="$1"
            ;;
        --sha256)
            shift
            (($# > 0)) || {
                alicia_toolchain_error '--sha256 requires a value.'
                exit 2
            }
            offline_sha256="$1"
            ;;
        --offline-only)
            offline_only=1
            ;;
        --install-dir)
            shift
            (($# > 0)) || {
                alicia_toolchain_error '--install-dir requires a value.'
                exit 2
            }
            install_directory="$1"
            ;;
        --cache-dir)
            shift
            (($# > 0)) || {
                alicia_toolchain_error '--cache-dir requires a value.'
                exit 2
            }
            cache_directory="$1"
            ;;
        --force)
            force=1
            ;;
        --help | -h)
            usage
            exit 0
            ;;
        *)
            alicia_toolchain_error "Unsupported argument: $1"
            usage >&2
            exit 2
            ;;
    esac

    shift
done

required_version="$(alicia_required_sdk_version)"
dotnet_os="$(alicia_detect_dotnet_os)"
dotnet_architecture="$(alicia_detect_dotnet_architecture)"
install_directory="$(realpath -m -- "$install_directory")"
cache_directory="$(realpath -m -- "$cache_directory")"
local_dotnet="$install_directory/dotnet"

if [[ -x "$local_dotnet" && $force -eq 0 ]]; then
    if "$SCRIPT_DIR/verify-dotnet.sh" \
        --path "$local_dotnet" \
        --quiet; then
        alicia_toolchain_success \
            "Repository-local .NET SDK is already compatible."
        exit 0
    fi

    alicia_toolchain_error \
        "An incompatible local installation exists: $install_directory"
    alicia_toolchain_error \
        'Use --force to replace it.'
    exit 1
fi

if [[ -z "$offline_archive" ]]; then
    cached_archive="$cache_directory/dotnet-sdk-$required_version-$dotnet_os-$dotnet_architecture.tar.gz"

    if [[ -f "$cached_archive" ]]; then
        offline_archive="$cached_archive"
    fi
fi

if [[ -n "$offline_archive" && -z "$offline_sha256" ]]; then
    sidecar="$offline_archive.sha256"

    if [[ -f "$sidecar" ]]; then
        offline_sha256="$(awk 'NR == 1 { print $1 }' "$sidecar")"
    fi
fi

parent_directory="$(dirname -- "$install_directory")"
mkdir -p -- "$parent_directory"
lock_directory="$install_directory.bootstrap.lock"

if ! mkdir -- "$lock_directory" 2>/dev/null; then
    alicia_toolchain_error \
        "Another bootstrap process holds the lock: $lock_directory"
    exit 1
fi

temporary_root="$(
    mktemp -d \
        "$parent_directory/.dotnet-bootstrap.XXXXXXXX"
)"
temporary_install="$temporary_root/install"
previous_install=''

cleanup()
{
    local exit_code=$?

    if [[ -n "$previous_install" && \
        -d "$previous_install" && \
        ! -e "$install_directory" ]]; then
        mv -- "$previous_install" "$install_directory"
    fi

    rm -rf -- "$temporary_root" "$lock_directory"
    exit "$exit_code"
}

trap cleanup EXIT INT TERM

install_from_archive()
{
    local archive_path="$1"
    local expected_hash="$2"

    archive_path="$(realpath -m -- "$archive_path")"

    [[ -f "$archive_path" ]] || {
        alicia_toolchain_error \
            "Offline SDK archive not found: $archive_path"
        return 1
    }

    [[ "$expected_hash" =~ ^[[:xdigit:]]{64}$ ]] || {
        alicia_toolchain_error \
            'A 64-character SHA-256 is required for offline archives.'
        return 1
    }

    actual_hash="$(sha256sum -- "$archive_path" | awk '{ print $1 }')"

    if [[ "${actual_hash,,}" != "${expected_hash,,}" ]]; then
        alicia_toolchain_error \
            "Offline SDK archive checksum mismatch."
        return 1
    fi

    alicia_toolchain_success \
        "Offline SDK archive checksum verified."

    python3 "$SCRIPT_DIR/safe_extract_archive.py" \
        "$archive_path" \
        "$temporary_install"
}

download_file()
{
    local url="$1"
    local destination="$2"

    if command -v curl >/dev/null 2>&1; then
        curl \
            --fail \
            --location \
            --silent \
            --show-error \
            --output "$destination" \
            "$url"
        return
    fi

    if command -v wget >/dev/null 2>&1; then
        wget \
            --quiet \
            --output-document="$destination" \
            "$url"
        return
    fi

    alicia_toolchain_error \
        'curl or wget is required for online installation.'
    return 1
}

install_from_signed_installer()
{
    command -v gpg >/dev/null 2>&1 || {
        alicia_toolchain_error \
            'gpg is required to verify dotnet-install.sh.'
        return 1
    }

    local installer_url="${DOTNET_INSTALL_SCRIPT_URL:-$DEFAULT_INSTALLER_URL}"
    local signature_url="${DOTNET_INSTALL_SIGNATURE_URL:-$DEFAULT_SIGNATURE_URL}"
    local key_url="${DOTNET_INSTALL_KEY_URL:-$DEFAULT_KEY_URL}"
    local installer="$temporary_root/dotnet-install.sh"
    local signature="$temporary_root/dotnet-install.sig"
    local public_key="$temporary_root/dotnet-install.asc"
    local gpg_home="$temporary_root/gnupg"

    mkdir -m 0700 -- "$gpg_home"

    download_file "$installer_url" "$installer"
    download_file "$signature_url" "$signature"
    download_file "$key_url" "$public_key"

    gpg \
        --batch \
        --homedir "$gpg_home" \
        --import "$public_key" \
        >/dev/null 2>&1

    imported_fingerprints="$(
        gpg \
            --batch \
            --homedir "$gpg_home" \
            --with-colons \
            --fingerprint \
            2>/dev/null |
            awk -F: '$1 == "fpr" { print $10 }'
    )"

    if ! grep -Fxq \
        "$INSTALLER_FINGERPRINT" \
        <<<"$imported_fingerprints"; then
        alicia_toolchain_error \
            'The Microsoft installer signing key fingerprint is unexpected.'
        return 1
    fi

    gpg \
        --batch \
        --homedir "$gpg_home" \
        --verify "$signature" "$installer"

    alicia_toolchain_success \
        'Microsoft dotnet-install.sh signature verified.'

    bash "$installer" \
        --version "$required_version" \
        --install-dir "$temporary_install" \
        --architecture "$dotnet_architecture" \
        --os "$dotnet_os" \
        --no-path
}

if [[ -n "$offline_archive" ]]; then
    install_from_archive \
        "$offline_archive" \
        "$offline_sha256"
elif ((offline_only == 1)); then
    alicia_toolchain_error \
        'No verified offline SDK archive is available.'
    exit 1
else
    install_from_signed_installer
fi

[[ -x "$temporary_install/dotnet" ]] || {
    alicia_toolchain_error \
        'The installed SDK does not contain an executable dotnet host.'
    exit 1
}

ALICIA_DOTNET="$temporary_install/dotnet" \
ALICIA_DOTNET_DISABLE_SYSTEM=1 \
    "$SCRIPT_DIR/verify-dotnet.sh" \
        --path "$temporary_install/dotnet" \
        --quiet

if [[ -e "$install_directory" ]]; then
    previous_install="$temporary_root/previous-install"
    mv -- "$install_directory" "$previous_install"
fi

mv -- "$temporary_install" "$install_directory"

ALICIA_DOTNET="$install_directory/dotnet" \
ALICIA_DOTNET_DISABLE_SYSTEM=1 \
    "$SCRIPT_DIR/verify-dotnet.sh" \
        --path "$install_directory/dotnet" \
        --quiet

rm -rf -- "$previous_install"
previous_install=''

alicia_toolchain_success \
    "Installed .NET SDK $required_version in $install_directory"

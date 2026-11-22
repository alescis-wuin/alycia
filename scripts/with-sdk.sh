#!/usr/bin/env bash
# Exécute une commande avec le SDK du dépôt, sans modifier l'installation globale.
set -euo pipefail
if (( $# == 0 )); then
    echo "Usage: bash scripts/with-sdk.sh commande [arguments...]" >&2
    exit 2
fi
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
version=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["sdk"]["version"])' "$root/global.json")
if [[ -z ${ALYCIA_SDK_ARCHIVE:-} ]] && [[ $(cd "$root" && dotnet --version 2>/dev/null || true) == "$version" ]]; then
    exec "$@"
fi
case "$(uname -s)/$(uname -m)" in
    Linux/x86_64) rid=linux-x64 ;;
    Linux/aarch64|Linux/arm64) rid=linux-arm64 ;;
    *) echo "Distribution automatique disponible pour Linux glibc x64 et arm64." >&2; exit 2 ;;
esac
digest=$(awk -v version="$version" -v rid="$rid" '$1 == version && $2 == rid {print $3}' "$root/scripts/sdk-linux.sha512")
[[ $digest =~ ^[[:xdigit:]]{128}$ ]] || { echo "SHA-512 du SDK $version $rid absent." >&2; exit 1; }
cache=${ALYCIA_SDK_HOME:-${XDG_DATA_HOME:-$HOME/.local/share}/alycia/dotnet}
mkdir -p "$cache"
cache=$(cd "$cache" && pwd)
destination="$cache/$version-$rid"
(
    flock -x 9
    if [[ ! -x "$destination/dotnet" ]]; then
        stage=$(mktemp -d "$cache/.installation-XXXXXXXX")
        trap 'rm -rf -- "$stage"' EXIT
        archive=${ALYCIA_SDK_ARCHIVE:-$stage/sdk.tar.gz}
        if [[ -z ${ALYCIA_SDK_ARCHIVE:-} ]]; then
            curl --fail --location --silent --show-error --proto '=https' --proto-redir '=https' \
                "https://builds.dotnet.microsoft.com/dotnet/Sdk/$version/dotnet-sdk-$version-$rid.tar.gz" -o "$archive"
        fi
        actual=$(sha512sum -- "$archive")
        [[ ${actual%% *} == "$digest" ]] || { echo "SHA-512 du SDK incorrect." >&2; exit 1; }
        mkdir "$stage/sdk"
        tar -xzf "$archive" -C "$stage/sdk"
        [[ $(cd "$root" && "$stage/sdk/dotnet" --version) == "$version" ]]
        mv -T "$stage/sdk" "$destination"
    fi
) 9>"$cache/installation.lock"
export DOTNET_ROOT="$destination" PATH="$destination:$PATH"
[[ $(cd "$root" && dotnet --version) == "$version" ]] || { echo "SDK local incompatible." >&2; exit 1; }
exec "$@"

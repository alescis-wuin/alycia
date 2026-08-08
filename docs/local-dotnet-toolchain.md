# Repository-local .NET toolchain

Alicia pins its SDK in `global.json` and provides a repository-owned
resolution, verification, bootstrap, and execution layer under
`scripts/toolchain/`.

## Resolution order

The resolver checks candidates in this order:

1. `ALICIA_DOTNET`, when explicitly provided;
2. `.dotnet/dotnet` in the repository;
3. a compatible `dotnet` host from `PATH`.

The selected SDK must satisfy `sdk.version` and `sdk.rollForward` from
`global.json`. The project currently requires SDK `10.0.110` with the
`latestPatch` policy.

`global.json` also searches `.dotnet` before `$host$`. This allows .NET 10 hosts
to resolve the local SDK for SDK commands while retaining the system
installation as a fallback.

## Standard commands

```bash
make toolchain-check
make toolchain-info
make build
make test
make verify
```

All local Makefile operations that invoke the .NET CLI use
`scripts/toolchain/dotnet.sh`. The wrapper does not modify the user's persistent
shell configuration or machine-wide `PATH`.

## Bootstrap from the signed online installer

```bash
make toolchain-bootstrap
```

The bootstrap script downloads Microsoft's installer and detached signature,
verifies the configured Microsoft signing key, installs the pinned SDK into
`.dotnet`, and validates the resulting host before replacing an existing local
installation.

The online path requires `gpg` and either `curl` or `wget`.

## Bootstrap from an offline SDK archive

Provide a trusted SDK archive and its SHA-256:

```bash
make toolchain-bootstrap \
  TOOLCHAIN_ARCHIVE="$HOME/Downloads/dotnet-sdk-10.0.110-linux-x64.tar.gz" \
  TOOLCHAIN_SHA256="<64-character-sha256>" \
  TOOLCHAIN_OFFLINE_ONLY=1
```

The checksum can instead be stored in an adjacent sidecar file named
`<archive>.sha256`. A cached archive can be placed at:

```text
.toolchain-cache/dotnet-sdk-10.0.110-<os>-<architecture>.tar.gz
```

Archive extraction rejects absolute paths, parent traversal, unsafe links, and
special files. Installation is prepared in a temporary directory and moved
into place only after verification.

## Replace or remove a local installation

Replace an existing incompatible installation:

```bash
make toolchain-bootstrap TOOLCHAIN_FORCE=1
```

Remove only the repository-local installation and bootstrap lock:

```bash
make toolchain-clean
```

The cache is intentionally preserved. Remove `.toolchain-cache` separately
when its archives are no longer required.

## Direct script usage

```bash
scripts/toolchain/resolve-dotnet.sh --json
scripts/toolchain/verify-dotnet.sh
scripts/toolchain/dotnet.sh --info
scripts/toolchain/tests/run.sh
scripts/toolchain/tests/integration.sh
```

## Local directories

The following paths are ignored by Git:

```text
.dotnet/
.dotnet.bootstrap.lock/
.toolchain-cache/
.support/
```

Do not commit SDK archives, extracted SDK files, support bundles, or bootstrap
locks.

## Patch workflow

The patch runner no longer requires a system `dotnet` command. It verifies the
repository toolchain before extracting and applying a patch. When no compatible
SDK is available, run:

```bash
make toolchain-bootstrap
```

and retry the patch command.

## Continuous integration

GitHub Actions continues to install the pinned SDK with `actions/setup-dotnet`.
The local wrapper remains the source of truth for Makefile commands and local
patch validation. Cross-platform workflow commands may invoke the SDK installed
by the action directly, especially on Windows where the repository toolchain
scripts are Bash-based.

## Troubleshooting

Display the selected host, source, version, and SDK details:

```bash
make toolchain-info
```

Force a specific candidate without changing persistent configuration:

```bash
ALICIA_DOTNET=/absolute/path/to/dotnet \
  scripts/toolchain/verify-dotnet.sh
```

Disable system fallback while diagnosing local installation behavior:

```bash
ALICIA_DOTNET_DISABLE_SYSTEM=1 \
  scripts/toolchain/resolve-dotnet.sh --json
```

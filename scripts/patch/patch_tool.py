#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import subprocess
import sys
import zipfile
from pathlib import Path, PurePosixPath
from typing import Any, Iterable

SCHEMA_VERSION = 1
BRANCH_PATTERN = re.compile(
    r"^(?:agent/)?(?:feature|fix|chore|test|docs|refactor|perf|build|ci|style|release|hotfix)/[a-z0-9][a-z0-9-]*$"
)
COMMIT_HEADER_PATTERN = re.compile(
    r"^(feature|fix|chore|test|docs|refactor|perf|build|ci|style|release|hotfix)\(([a-z0-9][a-z0-9-]*)\): (.+)$"
)
FOOTER_PATTERN = re.compile(
    r"^(Refs|Closes|Fixes|Issue|Ticket|BREAKING CHANGE): .+$"
)
HEX_256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
PATCH_NAME_PATTERN = re.compile(r"^[a-z0-9][a-z0-9-]*$")
ALLOWED_KINDS = {"shell", "patch", "json", "text", "source", "binary"}
MAX_ARCHIVE_BYTES = 200 * 1024 * 1024


class PatchValidationError(RuntimeError):
    pass


def fail(message: str) -> None:
    raise PatchValidationError(message)


def load_json(path: Path) -> dict[str, Any]:
    try:
        with path.open(encoding="utf-8-sig") as stream:
            value = json.load(stream)
    except (OSError, json.JSONDecodeError) as exception:
        fail(f"Invalid JSON file {path}: {exception}")

    if not isinstance(value, dict):
        fail(f"JSON root must be an object: {path}")

    return value


def require_mapping(parent: dict[str, Any], key: str) -> dict[str, Any]:
    value = parent.get(key)
    if not isinstance(value, dict):
        fail(f"'{key}' must be an object")
    return value


def require_list(parent: dict[str, Any], key: str) -> list[Any]:
    value = parent.get(key)
    if not isinstance(value, list):
        fail(f"'{key}' must be an array")
    return value


def require_string(parent: dict[str, Any], key: str, *, allow_empty: bool = False) -> str:
    value = parent.get(key)
    if not isinstance(value, str):
        fail(f"'{key}' must be a string")
    if not allow_empty and not value.strip():
        fail(f"'{key}' must not be empty")
    if "\x00" in value or "\n" in value or "\r" in value:
        fail(f"'{key}' contains a forbidden control character")
    return value


def require_bool(parent: dict[str, Any], key: str) -> bool:
    value = parent.get(key)
    if not isinstance(value, bool):
        fail(f"'{key}' must be a boolean")
    return value


def validate_relative_path(value: str, field: str) -> str:
    path = PurePosixPath(value)
    if path.is_absolute() or not path.parts or any(part in {"", ".", ".."} for part in path.parts):
        fail(f"'{field}' must be a safe relative POSIX path: {value}")
    return value


def validate_string_list(values: list[Any], field: str) -> list[str]:
    result: list[str] = []
    for index, value in enumerate(values):
        if not isinstance(value, str) or not value:
            fail(f"'{field}[{index}]' must be a non-empty string")
        if "\n" in value or "\r" in value or "\x00" in value:
            fail(f"'{field}[{index}]' contains a forbidden control character")
        result.append(value)
    return result


def validate_status_section(section: dict[str, Any], field: str) -> None:
    status = require_mapping(section, "status")
    mode = require_string(status, "mode")
    if mode not in {"exact", "contains"}:
        fail(f"'{field}.status.mode' must be 'exact' or 'contains'")
    entries = validate_string_list(require_list(status, "entries"), f"{field}.status.entries")
    expected_count = status.get("expected_count", len(entries))
    if not isinstance(expected_count, int) or expected_count < 0:
        fail(f"'{field}.status.expected_count' must be a non-negative integer")
    if mode == "exact" and expected_count != len(entries):
        fail(f"'{field}.status.expected_count' must equal the exact entry count")


def target_branch(repository: dict[str, Any]) -> str:
    branch = require_mapping(repository, "branch")
    branch_type = require_string(branch, "type")
    branch_name = require_string(branch, "name")
    prefix = branch.get("prefix")
    if prefix is not None and prefix != "agent":
        fail("'repository.branch.prefix' must be null or 'agent'")
    return f"{prefix + '/' if prefix else ''}{branch_type}/{branch_name}"


def validate_manifest(manifest: dict[str, Any], *, allow_empty_hashes: bool = False) -> None:
    if manifest.get("schema_version") != SCHEMA_VERSION:
        fail(f"'schema_version' must be {SCHEMA_VERSION}")

    patch = require_mapping(manifest, "patch")
    patch_name = require_string(patch, "name")
    if not PATCH_NAME_PATTERN.fullmatch(patch_name):
        fail("'patch.name' must use lowercase letters, digits, and hyphens")
    require_string(patch, "description")

    repository = require_mapping(manifest, "repository")
    require_string(repository, "project")
    require_string(repository, "root_name")
    start_branch = require_string(repository, "start_branch")
    end_branch = require_string(repository, "end_branch")
    require_string(repository, "base_ref")
    branch = require_mapping(repository, "branch")
    require_bool(branch, "create")
    existing_policy = require_string(branch, "existing_policy")
    if existing_policy not in {"fail", "require"}:
        fail("'repository.branch.existing_policy' must be 'fail' or 'require'")
    computed_target = target_branch(repository)
    if end_branch != computed_target:
        fail("'repository.end_branch' does not match branch type/name")
    if start_branch != end_branch and not BRANCH_PATTERN.fullmatch(end_branch):
        fail("'repository.end_branch' violates repository branch conventions")

    apply = require_mapping(manifest, "apply")
    validate_relative_path(require_string(apply, "entrypoint"), "apply.entrypoint")

    package = require_mapping(manifest, "package")
    max_uncompressed = package.get("max_uncompressed_bytes")
    if not isinstance(max_uncompressed, int) or max_uncompressed <= 0:
        fail("'package.max_uncompressed_bytes' must be a positive integer")
    files = require_list(package, "files")
    seen_paths: set[str] = set()
    for index, item in enumerate(files):
        if not isinstance(item, dict):
            fail(f"'package.files[{index}]' must be an object")
        file_path = validate_relative_path(
            require_string(item, "path"), f"package.files[{index}].path"
        )
        if file_path in {"manifest.json", "SHA256SUMS"}:
            fail("manifest.json and SHA256SUMS must not appear in package.files")
        if file_path in seen_paths:
            fail(f"Duplicate package file: {file_path}")
        seen_paths.add(file_path)
        kind = require_string(item, "kind")
        if kind not in ALLOWED_KINDS:
            fail(f"Unsupported package file kind: {kind}")
        require_bool(item, "non_empty")
        checksum = item.get("sha256")
        if allow_empty_hashes and checksum in {None, ""}:
            continue
        if not isinstance(checksum, str) or not HEX_256_PATTERN.fullmatch(checksum):
            fail(f"Invalid SHA-256 checksum for {file_path}")

    preconditions = require_mapping(manifest, "preconditions")
    validate_status_section(preconditions, "preconditions")
    validate_string_list(require_list(preconditions, "required_paths"), "preconditions.required_paths")

    postconditions = require_mapping(manifest, "postconditions")
    validate_status_section(postconditions, "postconditions")
    changed_paths = validate_string_list(
        require_list(postconditions, "changed_paths"), "postconditions.changed_paths"
    )
    for value in changed_paths:
        validate_relative_path(value, "postconditions.changed_paths")
    changed_mode = require_string(postconditions, "changed_paths_mode")
    if changed_mode not in {"exact", "contains"}:
        fail("'postconditions.changed_paths_mode' must be 'exact' or 'contains'")

    validation = require_mapping(manifest, "validation")
    commands = require_list(validation, "commands")
    if not commands:
        fail("'validation.commands' must not be empty")
    for index, command in enumerate(commands):
        if not isinstance(command, dict):
            fail(f"'validation.commands[{index}]' must be an object")
        require_string(command, "name")
        require_string(command, "command")
    require_bool(validation, "run_all")

    manual_test = require_mapping(manifest, "manual_test")
    mode = require_string(manual_test, "mode")
    if mode not in {"never", "optional", "required"}:
        fail("'manual_test.mode' must be never, optional, or required")
    require_string(manual_test, "command", allow_empty=mode == "never")
    require_string(manual_test, "prompt", allow_empty=mode == "never")
    require_string(manual_test, "acceptance_prompt", allow_empty=mode == "never")

    stage = require_mapping(manifest, "stage")
    stage_paths = validate_string_list(require_list(stage, "paths"), "stage.paths")
    if not stage_paths:
        fail("'stage.paths' must not be empty")
    for value in stage_paths:
        validate_relative_path(value, "stage.paths")

    commit = require_mapping(manifest, "commit")
    enabled = require_bool(commit, "enabled")
    require_bool(commit, "sign")
    mode = require_string(commit, "mode")
    if mode not in {"automatic", "prompt"}:
        fail("'commit.mode' must be automatic or prompt")
    header = require_string(commit, "header", allow_empty=not enabled)
    body = validate_string_list(require_list(commit, "body"), "commit.body")
    footer = require_string(commit, "footer", allow_empty=not enabled)
    if enabled:
        match = COMMIT_HEADER_PATTERN.fullmatch(header)
        if match is None:
            fail("'commit.header' does not follow '<type>(<scope>): <title>'")
        branch = require_mapping(repository, "branch")
        if match.group(1) != branch["type"] or match.group(2) != branch["name"]:
            fail("Commit type/scope must match the target branch type/name")
        if not body or any(not line.startswith(("-", "*")) for line in body):
            fail("Every commit body line must start with '-' or '*'")
        if not FOOTER_PATTERN.fullmatch(footer):
            fail("'commit.footer' does not follow repository conventions")
        for line in [header, *body, footer]:
            if len(line) > 120:
                fail("Commit message lines must not exceed 120 characters")

    post_commit = require_mapping(manifest, "post_commit")
    commands = require_list(post_commit, "commands")
    for index, command in enumerate(commands):
        if not isinstance(command, dict):
            fail(f"'post_commit.commands[{index}]' must be an object")
        require_string(command, "name")
        require_string(command, "command")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def validate_kind(path: Path, kind: str) -> None:
    suffix = path.suffix.lower()
    if kind == "shell" and suffix != ".sh":
        fail(f"Shell file must use .sh: {path}")
    if kind == "patch" and suffix not in {".patch", ".diff"}:
        fail(f"Patch file must use .patch or .diff: {path}")
    if kind == "json" and suffix != ".json":
        fail(f"JSON file must use .json: {path}")
    if kind == "text" and suffix not in {".md", ".txt"}:
        fail(f"Text file must use .md or .txt: {path}")

    text_content = None
    if kind in {"shell", "patch", "json", "text", "source"}:
        try:
            text_content = path.read_text(encoding="utf-8")
        except UnicodeDecodeError as exception:
            fail(f"Text file is not valid UTF-8: {path}: {exception}")

    if kind == "shell":
        result = subprocess.run(
            ["bash", "-n", str(path)],
            check=False,
            capture_output=True,
            text=True,
        )
        if result.returncode != 0:
            fail(f"Invalid shell syntax in {path}: {result.stderr.strip()}")

    if kind == "patch" and text_content is not None:
        if "diff --git " not in text_content and not ("--- " in text_content and "+++ " in text_content):
            fail(f"Patch file does not contain a recognizable unified diff: {path}")

    if kind == "json":
        load_json(path)


def expected_checksum_lines(package_dir: Path, manifest: dict[str, Any]) -> list[str]:
    paths = [Path("manifest.json")]
    paths.extend(Path(item["path"]) for item in manifest["package"]["files"])
    return [f"{sha256(package_dir / path)}  {path.as_posix()}" for path in sorted(paths)]


def validate_package(package_dir: Path) -> dict[str, Any]:
    manifest_path = package_dir / "manifest.json"
    sums_path = package_dir / "SHA256SUMS"
    if not manifest_path.is_file():
        fail(f"Missing manifest.json in {package_dir}")
    if not sums_path.is_file():
        fail(f"Missing SHA256SUMS in {package_dir}")

    manifest = load_json(manifest_path)
    validate_manifest(manifest)

    expected_paths = {item["path"] for item in manifest["package"]["files"]}
    actual_paths = {
        path.relative_to(package_dir).as_posix()
        for path in package_dir.rglob("*")
        if path.is_file()
        and path.relative_to(package_dir).as_posix() not in {"manifest.json", "SHA256SUMS"}
    }
    if actual_paths != expected_paths:
        missing = sorted(expected_paths - actual_paths)
        extra = sorted(actual_paths - expected_paths)
        fail(f"Package file declaration mismatch. Missing={missing}, extra={extra}")

    total_size = 0
    for item in manifest["package"]["files"]:
        path = package_dir / item["path"]
        if not path.is_file():
            fail(f"Declared package file is missing: {item['path']}")
        size = path.stat().st_size
        total_size += size
        if item["non_empty"] and size == 0:
            fail(f"Declared package file is empty: {item['path']}")
        validate_kind(path, item["kind"])
        actual_hash = sha256(path)
        if actual_hash != item["sha256"]:
            fail(f"Manifest checksum mismatch: {item['path']}")

    if total_size > manifest["package"]["max_uncompressed_bytes"]:
        fail("Package payload exceeds manifest maximum size")

    entrypoint = package_dir / manifest["apply"]["entrypoint"]
    if not entrypoint.is_file():
        fail("Apply entrypoint is missing")

    expected_lines = expected_checksum_lines(package_dir, manifest)
    actual_lines = [line.rstrip("\n") for line in sums_path.read_text(encoding="utf-8").splitlines() if line]
    if actual_lines != expected_lines:
        fail("SHA256SUMS does not exactly match the package contents")

    return manifest


def safe_extract(archive: Path, destination: Path) -> Path:
    destination.mkdir(parents=True, exist_ok=True)
    total_size = 0
    seen: set[str] = set()
    roots: set[str] = set()

    with zipfile.ZipFile(archive) as bundle:
        for info in bundle.infolist():
            if info.flag_bits & 0x1:
                fail(f"Encrypted ZIP entries are not supported: {info.filename}")
            path = PurePosixPath(info.filename)
            if path.is_absolute() or any(part in {"", ".", ".."} for part in path.parts):
                fail(f"Unsafe ZIP entry path: {info.filename}")
            if info.filename in seen:
                fail(f"Duplicate ZIP entry: {info.filename}")
            seen.add(info.filename)
            roots.add(path.parts[0])
            mode = (info.external_attr >> 16) & 0o170000
            if stat.S_ISLNK(mode):
                fail(f"Symbolic links are forbidden in patch archives: {info.filename}")
            total_size += info.file_size
            if total_size > MAX_ARCHIVE_BYTES:
                fail("Patch archive exceeds the hard uncompressed size limit")

        if len(roots) != 1:
            fail("Patch archive must contain exactly one top-level directory")

        root_name = next(iter(roots))
        root_path = destination / root_name
        for info in bundle.infolist():
            target = destination.joinpath(*PurePosixPath(info.filename).parts)
            if info.is_dir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with bundle.open(info) as source, target.open("wb") as output:
                while block := source.read(1024 * 1024):
                    output.write(block)
            mode = (info.external_attr >> 16) & 0o777
            if mode:
                target.chmod(mode)

    if not (root_path / "manifest.json").is_file():
        fail("Patch archive must contain manifest.json at its top-level directory")
    return root_path


def get_path(value: Any, dotted_path: str) -> Any:
    current = value
    for component in dotted_path.split("."):
        if not isinstance(current, dict) or component not in current:
            fail(f"Manifest path not found: {dotted_path}")
        current = current[component]
    return current


def render_commit(manifest: dict[str, Any]) -> str:
    commit = manifest["commit"]
    lines = [commit["header"], "", *commit["body"], "", commit["footer"]]
    return "\n".join(lines) + "\n"


def status_paths(lines: Iterable[str]) -> list[str]:
    paths: list[str] = []
    for line in lines:
        if not line:
            continue
        if len(line) < 4:
            fail(f"Invalid git status line: {line!r}")
        path = line[3:]
        if " -> " in path:
            path = path.split(" -> ", 1)[1]
        paths.append(path.strip('"'))
    return paths


def compare_values(actual: list[str], expected: list[str], mode: str, label: str) -> None:
    actual_set = set(actual)
    expected_set = set(expected)
    if mode == "exact" and actual_set != expected_set:
        fail(f"{label} mismatch. Expected={sorted(expected_set)}, actual={sorted(actual_set)}")
    if mode == "contains" and not expected_set.issubset(actual_set):
        fail(f"{label} is missing expected entries: {sorted(expected_set - actual_set)}")


def update_checksums(package_dir: Path, manifest: dict[str, Any]) -> dict[str, Any]:
    validate_manifest(manifest, allow_empty_hashes=True)
    expected = {item["path"] for item in manifest["package"]["files"]}
    actual = {
        path.relative_to(package_dir).as_posix()
        for path in package_dir.rglob("*")
        if path.is_file()
        and path.relative_to(package_dir).as_posix() not in {"manifest.json", "SHA256SUMS"}
    }
    if actual != expected:
        fail(f"Package file declaration mismatch before packing. Missing={sorted(expected-actual)}, extra={sorted(actual-expected)}")
    for item in manifest["package"]["files"]:
        item["sha256"] = sha256(package_dir / item["path"])
    manifest_path = package_dir / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    sums = expected_checksum_lines(package_dir, manifest)
    (package_dir / "SHA256SUMS").write_text("\n".join(sums) + "\n", encoding="utf-8")
    return manifest


def pack_package(package_dir: Path, output: Path | None) -> Path:
    manifest = update_checksums(package_dir, load_json(package_dir / "manifest.json"))
    validate_package(package_dir)
    if output is None:
        output = package_dir.parent / f"{manifest['patch']['name']}.zip"
    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists():
        output.unlink()
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
        for path in sorted(package_dir.rglob("*")):
            if path.is_file():
                arcname = Path(manifest["patch"]["name"]) / path.relative_to(package_dir)
                info = zipfile.ZipInfo.from_file(path, arcname.as_posix())
                info.compress_type = zipfile.ZIP_DEFLATED
                bundle.writestr(info, path.read_bytes())
    return output


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Alicia patch package utility")
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate = subparsers.add_parser("validate-dir")
    validate.add_argument("--package-dir", required=True, type=Path)

    extract = subparsers.add_parser("safe-extract")
    extract.add_argument("--archive", required=True, type=Path)
    extract.add_argument("--destination", required=True, type=Path)

    get = subparsers.add_parser("get")
    get.add_argument("--manifest", required=True, type=Path)
    get.add_argument("--path", required=True)

    list_parser = subparsers.add_parser("list")
    list_parser.add_argument("--manifest", required=True, type=Path)
    list_parser.add_argument("--path", required=True)

    rows = subparsers.add_parser("rows")
    rows.add_argument("--manifest", required=True, type=Path)
    rows.add_argument("--path", required=True)
    rows.add_argument("--fields", required=True)

    render = subparsers.add_parser("render-commit")
    render.add_argument("--manifest", required=True, type=Path)
    render.add_argument("--output", required=True, type=Path)

    status = subparsers.add_parser("validate-status")
    status.add_argument("--manifest", required=True, type=Path)
    status.add_argument("--section", choices=["preconditions", "postconditions"], required=True)
    status.add_argument("--status-file", required=True, type=Path)

    paths = subparsers.add_parser("validate-paths")
    paths.add_argument("--manifest", required=True, type=Path)
    paths.add_argument("--status-file", required=True, type=Path)

    pack = subparsers.add_parser("pack")
    pack.add_argument("--package-dir", required=True, type=Path)
    pack.add_argument("--output", type=Path)

    return parser


def main() -> int:
    parser = build_parser()
    arguments = parser.parse_args()
    try:
        if arguments.command == "validate-dir":
            manifest = validate_package(arguments.package_dir.resolve())
            print(f"Valid patch package: {manifest['patch']['name']}")
        elif arguments.command == "safe-extract":
            print(safe_extract(arguments.archive.resolve(), arguments.destination.resolve()))
        elif arguments.command == "get":
            value = get_path(load_json(arguments.manifest), arguments.path)
            if isinstance(value, bool):
                print("true" if value else "false")
            elif isinstance(value, (str, int, float)) or value is None:
                print("" if value is None else value)
            else:
                fail("Requested manifest value is not scalar")
        elif arguments.command == "list":
            value = get_path(load_json(arguments.manifest), arguments.path)
            if not isinstance(value, list) or any(not isinstance(item, str) for item in value):
                fail("Requested manifest value is not an array of strings")
            for item in value:
                print(item)
        elif arguments.command == "rows":
            value = get_path(load_json(arguments.manifest), arguments.path)
            fields = arguments.fields.split(",")
            if not isinstance(value, list):
                fail("Requested manifest value is not an array")
            for item in value:
                if not isinstance(item, dict):
                    fail("Requested row value is not an object")
                cells = []
                for field in fields:
                    cell = item.get(field)
                    if not isinstance(cell, str) or "\t" in cell or "\n" in cell:
                        fail(f"Invalid row field: {field}")
                    cells.append(cell)
                print("\t".join(cells))
        elif arguments.command == "render-commit":
            manifest = load_json(arguments.manifest)
            validate_manifest(manifest)
            arguments.output.write_text(render_commit(manifest), encoding="utf-8")
        elif arguments.command == "validate-status":
            manifest = load_json(arguments.manifest)
            section = manifest[arguments.section]["status"]
            actual = arguments.status_file.read_text(encoding="utf-8").splitlines()
            compare_values(actual, section["entries"], section["mode"], f"{arguments.section} status")
            if len(actual) != section.get("expected_count", len(section["entries"])):
                fail(f"{arguments.section} status count mismatch")
        elif arguments.command == "validate-paths":
            manifest = load_json(arguments.manifest)
            actual = status_paths(arguments.status_file.read_text(encoding="utf-8").splitlines())
            compare_values(
                actual,
                manifest["postconditions"]["changed_paths"],
                manifest["postconditions"]["changed_paths_mode"],
                "postcondition changed paths",
            )
        elif arguments.command == "pack":
            print(pack_package(arguments.package_dir.resolve(), arguments.output.resolve() if arguments.output else None))
        else:
            parser.error("Unknown command")
    except PatchValidationError as exception:
        print(f"ERROR: {exception}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

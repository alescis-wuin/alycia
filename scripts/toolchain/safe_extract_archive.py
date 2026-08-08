#!/usr/bin/env python3
from __future__ import annotations

import argparse
import os
from pathlib import Path, PurePosixPath
import shutil
import tarfile


class ArchiveError(RuntimeError):
    pass


def validate_member(member: tarfile.TarInfo) -> None:
    if member.name in {".", "./"} and member.isdir():
        return

    path = PurePosixPath(member.name)

    if path.is_absolute() or not path.parts:
        raise ArchiveError(f"Unsafe archive path: {member.name}")

    if any(part in {"", ".", ".."} for part in path.parts):
        raise ArchiveError(f"Unsafe archive path: {member.name}")

    if member.ischr() or member.isblk() or member.isfifo():
        raise ArchiveError(f"Unsupported special file: {member.name}")

    if member.issym():
        target = path.parent / PurePosixPath(member.linkname)
        if target.is_absolute() or ".." in target.parts:
            raise ArchiveError(f"Unsafe symbolic link: {member.name}")

    if member.islnk():
        target = PurePosixPath(member.linkname)
        if target.is_absolute() or ".." in target.parts:
            raise ArchiveError(f"Unsafe hard link: {member.name}")


def extract_archive(archive: Path, destination: Path) -> None:
    if destination.exists() and any(destination.iterdir()):
        raise ArchiveError(
            f"Extraction destination must be empty: {destination}"
        )

    destination.mkdir(parents=True, exist_ok=True)

    with tarfile.open(archive, mode="r:*") as bundle:
        members = bundle.getmembers()

        for member in members:
            validate_member(member)

        try:
            bundle.extractall(
                destination,
                members=members,
                filter="data",
            )
        except (tarfile.TarError, OSError) as exception:
            raise ArchiveError(str(exception)) from exception

    destination_root = destination.resolve()

    for path in destination.rglob("*"):
        if not path.is_symlink():
            continue

        resolved = path.resolve(strict=False)

        try:
            resolved.relative_to(destination_root)
        except ValueError as exception:
            raise ArchiveError(
                f"Extracted link escapes destination: {path}"
            ) from exception


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Safely extract a repository-local .NET SDK archive."
    )
    parser.add_argument("archive", type=Path)
    parser.add_argument("destination", type=Path)
    arguments = parser.parse_args()

    if not arguments.archive.is_file():
        raise ArchiveError(f"Archive not found: {arguments.archive}")

    try:
        extract_archive(
            arguments.archive.resolve(),
            arguments.destination.resolve(),
        )
    except BaseException:
        if arguments.destination.exists():
            shutil.rmtree(arguments.destination)
        raise

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

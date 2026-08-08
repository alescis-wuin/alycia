#!/usr/bin/env python3
from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import Path


def write_atomic(path: Path, value: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = path.with_name(f"{path.name}.tmp.{os.getpid()}")
    temporary_path.write_text(value, encoding="utf-8")
    temporary_path.replace(path)


def supervise(
    working_directory: Path,
    command: str,
    status_file: Path,
) -> int:
    completed = subprocess.run(
        [
            "bash",
            "-Eeuo",
            "pipefail",
            "-c",
            command,
        ],
        cwd=working_directory,
        check=False,
    )

    write_atomic(
        status_file,
        f"{completed.returncode}\n",
    )

    return completed.returncode


def start(
    working_directory: Path,
    command: str,
    log_file: Path,
    pid_file: Path,
    status_file: Path,
) -> int:
    working_directory = working_directory.resolve()
    log_file = log_file.resolve()
    pid_file = pid_file.resolve()
    status_file = status_file.resolve()

    if not working_directory.is_dir():
        raise RuntimeError(
            "Manual command working directory "
            f"does not exist: {working_directory}"
        )

    log_file.parent.mkdir(parents=True, exist_ok=True)
    pid_file.parent.mkdir(parents=True, exist_ok=True)
    status_file.parent.mkdir(parents=True, exist_ok=True)

    pid_file.unlink(missing_ok=True)
    status_file.unlink(missing_ok=True)

    with log_file.open("ab") as log_stream:
        process = subprocess.Popen(
            [
                sys.executable,
                str(Path(__file__).resolve()),
                "supervise",
                "--cwd",
                str(working_directory),
                "--command",
                command,
                "--status-file",
                str(status_file),
            ],
            stdin=subprocess.DEVNULL,
            stdout=log_stream,
            stderr=subprocess.STDOUT,
            start_new_session=True,
            close_fds=True,
        )

    write_atomic(
        pid_file,
        f"{process.pid}\n",
    )

    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Start and supervise an isolated manual "
            "patch validation process."
        )
    )

    subparsers = parser.add_subparsers(
        dest="action",
        required=True,
    )

    start_parser = subparsers.add_parser("start")
    start_parser.add_argument("--cwd", required=True, type=Path)
    start_parser.add_argument("--command", required=True)
    start_parser.add_argument("--log-file", required=True, type=Path)
    start_parser.add_argument("--pid-file", required=True, type=Path)
    start_parser.add_argument("--status-file", required=True, type=Path)

    supervise_parser = subparsers.add_parser("supervise")
    supervise_parser.add_argument("--cwd", required=True, type=Path)
    supervise_parser.add_argument("--command", required=True)
    supervise_parser.add_argument("--status-file", required=True, type=Path)

    return parser


def main() -> int:
    arguments = build_parser().parse_args()

    if arguments.action == "start":
        return start(
            arguments.cwd,
            arguments.command,
            arguments.log_file,
            arguments.pid_file,
            arguments.status_file,
        )

    if arguments.action == "supervise":
        return supervise(
            arguments.cwd,
            arguments.command,
            arguments.status_file,
        )

    raise RuntimeError(
        f"Unsupported action: {arguments.action}"
    )


if __name__ == "__main__":
    raise SystemExit(main())

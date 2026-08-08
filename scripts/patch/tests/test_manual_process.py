from __future__ import annotations

import os
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path


MANUAL_PROCESS_TOOL = (
    Path(__file__).resolve().parents[1] /
    "manual_process.py"
)


class ManualProcessTests(unittest.TestCase):
    def test_successful_command_records_zero_status(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)

            pid_file = root / "process.pid"
            status_file = root / "process.status"
            log_file = root / "process.log"
            result_file = root / "result.txt"

            self._start(
                root,
                (
                    "printf '%s\\n' success > "
                    f"{self._quote(result_file)}"
                ),
                log_file,
                pid_file,
                status_file,
            )

            self._wait_for_file(status_file)

            self.assertEqual(
                "0\n",
                status_file.read_text(encoding="utf-8"),
            )

            self.assertEqual(
                "success\n",
                result_file.read_text(encoding="utf-8"),
            )

    def test_failed_command_records_exit_status(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)

            pid_file = root / "process.pid"
            status_file = root / "process.status"
            log_file = root / "process.log"

            self._start(
                root,
                "exit 7",
                log_file,
                pid_file,
                status_file,
            )

            self._wait_for_file(status_file)

            self.assertEqual(
                "7\n",
                status_file.read_text(encoding="utf-8"),
            )

    def test_manual_command_uses_dedicated_session(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)

            pid_file = root / "process.pid"
            status_file = root / "process.status"
            log_file = root / "process.log"
            ready_file = root / "ready.txt"
            stopped_file = root / "stopped.txt"

            command = " ".join(
                [
                    (
                        "trap "
                        f"\"printf '%s\\\\n' stopped > "
                        f"{self._quote(stopped_file)}; "
                        "exit 0\" TERM;"
                    ),
                    (
                        "printf '%s\\n' ready > "
                        f"{self._quote(ready_file)};"
                    ),
                    "while :; do sleep 1; done",
                ]
            )

            self._start(
                root,
                command,
                log_file,
                pid_file,
                status_file,
            )

            self._wait_for_file(ready_file)

            process_id = int(
                pid_file.read_text(encoding="utf-8").strip()
            )

            self.addCleanup(
                self._terminate_process_group,
                process_id,
            )

            self.assertEqual(
                process_id,
                os.getsid(process_id),
            )

            self.assertNotEqual(
                os.getsid(0),
                process_id,
            )

            os.killpg(
                process_id,
                signal.SIGTERM,
            )

            self._wait_for_file(stopped_file)
            self._wait_for_process_exit(process_id)

    def _start(
        self,
        working_directory: Path,
        command: str,
        log_file: Path,
        pid_file: Path,
        status_file: Path,
    ) -> None:
        completed = subprocess.run(
            [
                sys.executable,
                str(MANUAL_PROCESS_TOOL),
                "start",
                "--cwd",
                str(working_directory),
                "--command",
                command,
                "--log-file",
                str(log_file),
                "--pid-file",
                str(pid_file),
                "--status-file",
                str(status_file),
            ],
            check=False,
            capture_output=True,
            text=True,
        )

        self.assertEqual(
            0,
            completed.returncode,
            msg=(
                f"stdout:\n{completed.stdout}\n"
                f"stderr:\n{completed.stderr}"
            ),
        )

        self._wait_for_file(pid_file)

    def _wait_for_file(
        self,
        path: Path,
        timeout: float = 5.0,
    ) -> None:
        deadline = time.monotonic() + timeout

        while time.monotonic() < deadline:
            if path.is_file():
                return

            time.sleep(0.05)

        self.fail(
            f"Timed out waiting for file: {path}"
        )

    def _wait_for_process_exit(
        self,
        process_id: int,
        timeout: float = 5.0,
    ) -> None:
        deadline = time.monotonic() + timeout

        while time.monotonic() < deadline:
            try:
                os.kill(process_id, 0)
            except ProcessLookupError:
                return

            time.sleep(0.05)

        self.fail(
            "Timed out waiting for isolated "
            f"process {process_id} to exit."
        )

    @staticmethod
    def _terminate_process_group(
        process_id: int,
    ) -> None:
        try:
            os.killpg(
                process_id,
                signal.SIGKILL,
            )
        except ProcessLookupError:
            pass

    @staticmethod
    def _quote(path: Path) -> str:
        return (
            "'" +
            str(path).replace(
                "'",
                "'\"'\"'",
            ) +
            "'"
        )


if __name__ == "__main__":
    unittest.main()

from __future__ import annotations

import importlib.util
import json
import os
import shutil
import stat
import subprocess
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
PATCH_DIRECTORY = REPOSITORY_ROOT / "scripts" / "patch"
PATCH_TOOL_PATH = PATCH_DIRECTORY / "patch_tool.py"

SPEC = importlib.util.spec_from_file_location(
    "patch_tool_workflow_tests",
    PATCH_TOOL_PATH,
)
assert SPEC and SPEC.loader
patch_tool = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(patch_tool)


class PatchWorkflowTests(unittest.TestCase):
    def test_manual_test_runs_before_staging(self) -> None:
        runner = (
            PATCH_DIRECTORY /
            "apply-package.sh"
        ).read_text(encoding="utf-8")

        stages = [
            'patch_step APPLY ',
            'patch_step POST_INSPECTION ',
            'patch_step VALIDATE ',
            'patch_step MANUAL_TEST ',
            'patch_step STAGE ',
            'patch_step COMMIT ',
        ]

        positions = [
            runner.index(stage)
            for stage in stages
        ]

        self.assertEqual(
            sorted(positions),
            positions,
        )

        stage_start = runner.index(
            'patch_step STAGE '
        )
        commit_start = runner.index(
            'patch_step COMMIT '
        )

        self.assertIn(
            'patch_run_command staged-safety '
            '"make --no-print-directory staged"',
            runner[stage_start:commit_start],
        )

    def test_manual_test_uses_isolated_process(self) -> None:
        runner = (
            PATCH_DIRECTORY /
            "apply-package.sh"
        ).read_text(encoding="utf-8")

        self.assertIn(
            "patch_start_manual_process",
            runner,
        )
        self.assertIn(
            "patch_stop_manual_process",
            runner,
        )
        self.assertIn(
            "manual_process.py",
            runner,
        )

    def test_required_manual_rejection_keeps_changes_unstaged(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            temporary_root = Path(directory)
            repository = temporary_root / "alicia"
            downloads = temporary_root / "downloads"

            repository.mkdir()
            downloads.mkdir()

            self._initialize_repository(repository)
            self._copy_runner(repository)

            package_directory = (
                temporary_root /
                "alicia-manual-rejection-test"
            )
            archive = (
                downloads /
                "alicia-manual-rejection-test.zip"
            )

            self._create_required_manual_package(
                package_directory,
                archive,
            )

            runner = (
                repository /
                "scripts/patch/apply-package.sh"
            )

            environment = os.environ.copy()
            environment.update(
                {
                    "NO_COLOR": "1",
                    "PATCH": str(archive),
                    "PATCH_NON_INTERACTIVE": "1",
                    "PYTHONDONTWRITEBYTECODE": "1",
                }
            )

            completed = subprocess.run(
                [str(runner)],
                cwd=repository,
                env=environment,
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertNotEqual(
                0,
                completed.returncode,
                msg=completed.stdout + completed.stderr,
            )

            self.assertIn(
                "requires a manual application test "
                "before committing",
                completed.stdout + completed.stderr,
            )

            self.assertNotIn(
                "[STAGE]",
                completed.stdout + completed.stderr,
            )

            current_branch = self._git(
                repository,
                "branch",
                "--show-current",
            ).strip()

            self.assertEqual(
                "fix/patch-manual-test-safety",
                current_branch,
            )

            staged_paths = self._git(
                repository,
                "diff",
                "--cached",
                "--name-only",
            )

            self.assertEqual(
                "",
                staged_paths,
            )

            status = self._git(
                repository,
                "status",
                "--short",
                "--untracked-files=all",
            )

            self.assertEqual(
                "?? feature.txt\n",
                status,
            )

            subject = self._git(
                repository,
                "log",
                "-1",
                "--pretty=%s",
            ).strip()

            self.assertEqual(
                "test: install patch workflow",
                subject,
            )

    def _initialize_repository(
        self,
        repository: Path,
    ) -> None:
        subprocess.run(
            [
                "git",
                "-C",
                str(repository),
                "init",
                "--quiet",
                "--initial-branch=develop",
            ],
            check=True,
        )

        self._git(
            repository,
            "config",
            "user.name",
            "Alicia Tests",
        )
        self._git(
            repository,
            "config",
            "user.email",
            "tests@alicia.local",
        )
        self._git(
            repository,
            "config",
            "core.hooksPath",
            "/dev/null",
        )

        (repository / ".gitignore").write_text(
            "logs/\n",
            encoding="utf-8",
        )
        (repository / "baseline.txt").write_text(
            "baseline\n",
            encoding="utf-8",
        )

        self._git(
            repository,
            "add",
            "--",
            ".gitignore",
            "baseline.txt",
        )
        self._git(
            repository,
            "commit",
            "--quiet",
            "--no-gpg-sign",
            "--message",
            "test: initialize repository",
        )

    def _copy_runner(
        self,
        repository: Path,
    ) -> None:
        destination = (
            repository /
            "scripts/patch"
        )
        (destination / "lib").mkdir(
            parents=True,
        )

        for relative_path in [
            Path("apply-package.sh"),
            Path("manual_process.py"),
            Path("patch_tool.py"),
            Path("lib/logging.sh"),
        ]:
            source = PATCH_DIRECTORY / relative_path
            target = destination / relative_path

            shutil.copy2(
                source,
                target,
            )

            target.chmod(
                target.stat().st_mode |
                stat.S_IXUSR,
            )

        toolchain_verifier = (
            repository /
            "scripts/toolchain/verify-dotnet.sh"
        )
        toolchain_verifier.parent.mkdir(
            parents=True,
        )
        toolchain_verifier.write_text(
            "\n".join(
                [
                    "#!/usr/bin/env bash",
                    "set -Eeuo pipefail",
                    "exit 0",
                    "",
                ]
            ),
            encoding="utf-8",
        )
        toolchain_verifier.chmod(
            toolchain_verifier.stat().st_mode |
            stat.S_IXUSR,
        )

        self._git(
            repository,
            "add",
            "--",
            "scripts/patch/apply-package.sh",
            "scripts/patch/lib/logging.sh",
            "scripts/patch/manual_process.py",
            "scripts/patch/patch_tool.py",
            "scripts/toolchain/verify-dotnet.sh",
        )
        self._git(
            repository,
            "commit",
            "--quiet",
            "--no-gpg-sign",
            "--message",
            "test: install patch workflow",
        )

    def _create_required_manual_package(
        self,
        package_directory: Path,
        archive: Path,
    ) -> None:
        package_directory.mkdir()

        apply_script = (
            package_directory /
            "apply.sh"
        )

        apply_script.write_text(
            "\n".join(
                [
                    "#!/usr/bin/env bash",
                    "set -Eeuo pipefail",
                    'repository_root="$1"',
                    (
                        "printf '%s\\n' 'feature' "
                        '> "$repository_root/feature.txt"'
                    ),
                    "",
                ]
            ),
            encoding="utf-8",
        )

        apply_script.chmod(
            apply_script.stat().st_mode |
            stat.S_IXUSR,
        )

        manifest = {
            "schema_version": 1,
            "patch": {
                "name": "alicia-manual-rejection-test",
                "description": (
                    "Exercise required manual rejection."
                ),
            },
            "repository": {
                "project": "alicia",
                "root_name": "alicia",
                "start_branch": "develop",
                "end_branch": (
                    "fix/patch-manual-test-safety"
                ),
                "base_ref": "develop",
                "branch": {
                    "create": True,
                    "type": "fix",
                    "name": "patch-manual-test-safety",
                    "prefix": None,
                    "existing_policy": "fail",
                },
            },
            "apply": {
                "entrypoint": "apply.sh",
            },
            "package": {
                "max_uncompressed_bytes": 100000,
                "files": [
                    {
                        "path": "apply.sh",
                        "kind": "shell",
                        "non_empty": True,
                        "sha256": "",
                    }
                ],
            },
            "preconditions": {
                "status": {
                    "mode": "exact",
                    "expected_count": 0,
                    "entries": [],
                },
                "required_paths": [
                    ".gitignore",
                ],
            },
            "postconditions": {
                "status": {
                    "mode": "exact",
                    "expected_count": 1,
                    "entries": [
                        "?? feature.txt",
                    ],
                },
                "changed_paths_mode": "exact",
                "changed_paths": [
                    "feature.txt",
                ],
            },
            "validation": {
                "run_all": True,
                "commands": [
                    {
                        "name": "diff-check",
                        "command": "git diff --check",
                    }
                ],
            },
            "manual_test": {
                "mode": "required",
                "prompt": "Run the manual test?",
                "command": "true",
                "acceptance_prompt": (
                    "Accept the manual test?"
                ),
            },
            "stage": {
                "paths": [
                    "feature.txt",
                ],
            },
            "commit": {
                "enabled": True,
                "sign": True,
                "mode": "automatic",
                "header": (
                    "fix(patch-manual-test-safety): "
                    "test manual rejection"
                ),
                "body": [
                    "- exercise manual rejection",
                ],
                "footer": "Ticket: TEST-001",
            },
            "post_commit": {
                "commands": [],
            },
        }

        (
            package_directory /
            "manifest.json"
        ).write_text(
            json.dumps(
                manifest,
                ensure_ascii=False,
                indent=2,
            ) + "\n",
            encoding="utf-8",
        )

        patch_tool.pack_package(
            package_directory,
            archive,
        )

    def _git(
        self,
        repository: Path,
        *arguments: str,
    ) -> str:
        completed = subprocess.run(
            [
                "git",
                "-C",
                str(repository),
                *arguments,
            ],
            check=True,
            capture_output=True,
            text=True,
        )

        return completed.stdout


if __name__ == "__main__":
    unittest.main()

from __future__ import annotations

import importlib.util
import json
import os
import shlex
import stat
import subprocess
import tempfile
import unittest
import zipfile
from pathlib import Path

MODULE_PATH = Path(__file__).resolve().parents[1] / "patch_tool.py"
SPEC = importlib.util.spec_from_file_location("patch_tool", MODULE_PATH)
assert SPEC and SPEC.loader
patch_tool = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(patch_tool)


class PatchToolTests(unittest.TestCase):
    def valid_manifest(self) -> dict:
        return {
            "schema_version": 1,
            "patch": {"name": "alicia-example", "description": "Example."},
            "repository": {
                "project": "alicia",
                "root_name": "alicia",
                "start_branch": "develop",
                "end_branch": "feature/example",
                "base_ref": "origin/develop",
                "branch": {
                    "create": True,
                    "type": "feature",
                    "name": "example",
                    "prefix": None,
                    "existing_policy": "fail",
                },
            },
            "apply": {"entrypoint": "apply.sh"},
            "package": {
                "max_uncompressed_bytes": 100000,
                "files": [
                    {
                        "path": "apply.sh",
                        "kind": "shell",
                        "non_empty": True,
                        "sha256": "0" * 64,
                    }
                ],
            },
            "preconditions": {
                "status": {"mode": "exact", "expected_count": 0, "entries": []},
                "required_paths": ["Makefile"],
            },
            "postconditions": {
                "status": {"mode": "exact", "expected_count": 1, "entries": ["?? file"]},
                "changed_paths_mode": "exact",
                "changed_paths": ["file"],
            },
            "validation": {
                "run_all": True,
                "commands": [{"name": "build", "command": "make build"}],
            },
            "manual_test": {
                "mode": "never",
                "prompt": "",
                "command": "",
                "acceptance_prompt": "",
            },
            "stage": {"paths": ["file"]},
            "commit": {
                "enabled": True,
                "sign": True,
                "mode": "automatic",
                "header": "feature(example): add example",
                "body": ["- add example"],
                "footer": "Ticket: TEST-001",
            },
            "post_commit": {"commands": []},
        }

    def test_valid_manifest_is_accepted(self) -> None:
        patch_tool.validate_manifest(self.valid_manifest())

    def test_commit_scope_must_match_branch(self) -> None:
        manifest = self.valid_manifest()
        manifest["commit"]["header"] = "feature(other): add example"
        with self.assertRaises(patch_tool.PatchValidationError):
            patch_tool.validate_manifest(manifest)

    def test_safe_extract_rejects_traversal(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive = root / "unsafe.zip"
            with zipfile.ZipFile(archive, "w") as bundle:
                bundle.writestr("package/../escape.txt", "bad")
            with self.assertRaises(patch_tool.PatchValidationError):
                patch_tool.safe_extract(archive, root / "output")

    def test_status_path_parsing(self) -> None:
        self.assertEqual(
            ["a.txt", "new.txt"],
            patch_tool.status_paths([" M a.txt", "R  old.txt -> new.txt"]),
        )

    def test_commit_rendering(self) -> None:
        message = patch_tool.render_commit(self.valid_manifest())
        self.assertIn("feature(example): add example", message)
        self.assertTrue(message.endswith("Ticket: TEST-001\n"))

    def test_make_patch_uses_immutable_runtime_runner_copy(self) -> None:
        repository_root = Path(__file__).resolve().parents[3]
        makefile = (repository_root / "Makefile").read_text(encoding="utf-8")
        gitignore = (repository_root / ".gitignore").read_text(encoding="utf-8")

        self.assertIn(
            'runtime_runner="$(PATCH_RUNNER).runtime.$$$$.sh"',
            makefile,
        )
        self.assertIn(
            '"$$runtime_runner"',
            makefile,
        )
        self.assertIn(
            "scripts/patch/apply-package.sh.runtime.*.sh",
            gitignore,
        )

        with tempfile.TemporaryDirectory() as directory:
            temporary_root = Path(directory)
            source_runner = temporary_root / "apply-package.sh"
            result_file = temporary_root / "continued.txt"

            source_runner_quoted = shlex.quote(str(source_runner))
            result_file_quoted = shlex.quote(str(result_file))

            source_runner.write_text(
                "\n".join(
                    [
                        "#!/usr/bin/env bash",
                        "set -Eeuo pipefail",
                        f"readonly source_runner={source_runner_quoted}",
                        f"readonly result_file={result_file_quoted}",
                        '[[ "$0" != "$source_runner" ]] || exit 91',
                        '[[ "$0" == "$source_runner".runtime.*.sh ]] || exit 92',
                        "printf '%s\\n' '#!/usr/bin/env bash' 'exit 97' > \"$source_runner\"",
                        "chmod 700 \"$source_runner\"",
                        "printf '%s\\n' 'continued' > \"$result_file\"",
                        "",
                    ]
                ),
                encoding="utf-8",
            )
            source_runner.chmod(
                source_runner.stat().st_mode |
                stat.S_IXUSR,
            )

            environment = os.environ.copy()
            environment["PYTHONDONTWRITEBYTECODE"] = "1"

            completed = subprocess.run(
                [
                    "make",
                    "--no-print-directory",
                    "patch",
                    f"PATCH_RUNNER={source_runner}",
                    "PATCH=immutable-runner-test",
                    f"PATCH_DOWNLOADS_DIR={temporary_root}",
                ],
                cwd=repository_root,
                env=environment,
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
            self.assertEqual(
                "continued\n",
                result_file.read_text(encoding="utf-8"),
            )
            self.assertEqual(
                [],
                list(
                    temporary_root.glob(
                        "apply-package.sh.runtime.*.sh"
                    )
                ),
            )


if __name__ == "__main__":
    unittest.main()

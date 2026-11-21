import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tarfile
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("backup", Path(__file__).resolve().parents[1] / "backup.py")
backup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(backup)


class BackupTests(unittest.TestCase):
    def archive(self, directory, name="conversation/file.bin", link=False):
        directory = Path(directory)
        (directory / "database.dump").write_bytes(b"synthetic database")
        with tarfile.open(directory / "files.tar.gz", "w:gz") as tar:
            entry = tarfile.TarInfo(name)
            if link:
                entry.type = tarfile.SYMTYPE
                entry.linkname = "/outside"
                tar.addfile(entry)
            else:
                entry.size = 4
                tar.addfile(entry, io.BytesIO(b"safe"))
        manifest = {"schemaVersion": 1, "files": {name: {"bytes": (directory / name).stat().st_size,
            "sha256": backup.digest(directory / name)} for name in ("database.dump", "files.tar.gz")}}
        (directory / "manifest.json").write_text(json.dumps(manifest))
        return directory

    def test_verifies_payload_and_rejects_corruption(self):
        with tempfile.TemporaryDirectory() as directory:
            source = self.archive(directory)
            self.assertEqual(source.resolve(), backup.verify(source))
            (source / "database.dump").write_bytes(b"damaged")
            with self.assertRaises(ValueError):
                backup.verify(source)

    def test_rejects_archive_traversal_and_links(self):
        for name, link in [("../../outside", False), ("/outside", False), ("file", True)]:
            with self.subTest(name=name, link=link), tempfile.TemporaryDirectory() as directory:
                source = self.archive(directory, name, link)
                with self.assertRaises(ValueError):
                    backup.verify(source)

    def test_restore_refuses_an_existing_project_before_writing(self):
        with tempfile.TemporaryDirectory() as directory:
            source = self.archive(directory)
            with patch.object(backup, "run", return_value=SimpleNamespace(stdout="existing-container\n")) as command:
                with self.assertRaises(RuntimeError):
                    backup.restore(SimpleNamespace(source=source, project="alycia-restore-test", windows=False))
                self.assertEqual(1, command.call_count)
                self.assertEqual(["docker", "container", "ls"], command.call_args.args[0][:3])

    def test_failed_dump_restarts_only_the_previously_running_api(self):
        calls = []

        def execute(args, **kwargs):
            calls.append(args)
            if "--services" in args:
                return SimpleNamespace(stdout="postgres\nchat-api\n")
            if "exec" in args:
                raise subprocess.CalledProcessError(1, args)
            return SimpleNamespace(stdout="")

        with tempfile.TemporaryDirectory() as directory, patch.object(backup, "run", side_effect=execute):
            with self.assertRaises(subprocess.CalledProcessError):
                backup.backup(SimpleNamespace(output=directory, windows=False))
            self.assertEqual(["start", "chat-api"], calls[-1][-2:])


if __name__ == "__main__":
    unittest.main()

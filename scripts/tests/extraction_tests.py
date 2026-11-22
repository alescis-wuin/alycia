"""Mutate copies of the exported project manifests, never the checkout."""
import importlib.util
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from architecture.extraction import port_errors, validate_layout
from architecture.layout import projects

SPEC = importlib.util.spec_from_file_location("extraction_guard", ROOT / "scripts/check-modules.py")
guard = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(guard)


class ExtractionGuardTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        items = [ROOT/"modules.lock.json", ROOT/"Alycia.slnx", ROOT/"scripts/architecture/port-owners.tsv"]
        items.append(ROOT/"scripts/architecture/capture/Inventory.csproj")
        items.extend((ROOT/"modules").glob("*/*.slnx"))
        for p in projects(ROOT):
            items.extend([p, p.with_name("packages.lock.json")])
        for path in items:
            target = self.root / path.relative_to(ROOT)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)
        self.assertEqual([], validate_layout(self.root))

    def test_foreign_source_even_when_it_is_the_contracts_assembly(self):
        p = next((self.root/"modules/inference/src").glob("*/*.csproj"))
        tree = ET.parse(p)
        ET.SubElement(tree.find("ItemGroup"), "ProjectReference",
                      Include="../../../contracts/src/Alycia.Contracts/Alycia.Contracts.csproj")
        tree.write(p)
        self.assertTrue(any("inter-module source reference" in e for e in validate_layout(self.root)))

    def test_unlocked_internal_package(self):
        p = next((self.root/"modules/indexing/src").glob("*/*.csproj"))
        tree = ET.parse(p)
        tree.find(".//PackageReference[@Include='Alycia.Contracts']").set("Version", "0.*")
        tree.write(p)
        self.assertTrue(any("unlocked internal package" in e for e in validate_layout(self.root)))

    def test_missing_transitive_hash(self):
        p = next((self.root/"modules/platform/src").glob("*/packages.lock.json"))
        lock = json.loads(p.read_text())
        entry = next(x for x in lock["dependencies"]["net10.0"].values() if x["type"] == "Transitive")
        del entry["contentHash"]
        p.write_text(json.dumps(lock))
        self.assertTrue(any("incomplete package lock" in e for e in validate_layout(self.root)))

    def test_missing_module(self):
        (self.root/"modules/context").rename(self.root/"absent")
        self.assertTrue(any("missing or unexpected modules" in e for e in validate_layout(self.root)))

    def test_mock_in_release_graph(self):
        self.assertTrue(guard.forbidden("Alycia.Api", "Alycia.Modules.Inference.Mocks"))
        p = self.root/"src/Alycia.Api/packages.lock.json"
        lock = json.loads(p.read_text())
        lock["dependencies"]["net10.0"]["Alycia.Modules.Inference.Mocks"] = {"type": "Project"}
        p.write_text(json.dumps(lock))
        self.assertTrue(any("test dependency in production" in e for e in validate_layout(self.root)))

    def test_unowned_public_port(self):
        known = {l.split("\t")[0] for l in (self.root/"scripts/architecture/port-owners.tsv").read_text().splitlines()[1:]}
        self.assertEqual([], port_errors(self.root, known))
        self.assertIn("port without unique owner: Alycia.Contracts.IForeign", port_errors(self.root, known | {"Alycia.Contracts.IForeign"}))

    def test_modified_and_missing_historical_migration(self):
        path, digest = next(iter(guard.MIGRATION_HASHES.items()))
        physical = self.root/"modules/workspaces"/path
        physical.parent.mkdir(parents=True, exist_ok=True)
        physical.write_bytes(b"changed snapshot")
        self.assertIn("historical migration changed: "+path, guard.migration_errors(self.root, {path: digest}))
        physical.unlink()
        self.assertIn("missing historical migration: "+path, guard.migration_errors(self.root, {path: digest}))

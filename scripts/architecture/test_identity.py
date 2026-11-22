"""Reconcile signed VSTest cases against explicit changes of owning test assembly."""
import csv
import hashlib
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[2]
DOC = ROOT / "docs/architecture/preparation-multidepots"
BASE = ROOT / "docs/architecture/decouplage/cloture/recette"


def move_map():
    with (DOC / "TEST-CLASSES.tsv").open() as f:
        return {r["class"]: (r["old"], r["new"]) for r in csv.DictReader(f, delimiter="\t")}


def records(tree):
    definitions = {t.attrib["id"]: t for t in tree.findall(".//{*}UnitTest")}
    for result in tree.findall(".//{*}UnitTestResult"):
        tid, name = result.attrib["testId"], result.attrib["testName"]
        test = definitions[tid]
        method = test.find("{*}TestMethod")
        asm = test.attrib["storage"].replace("\\", "/").rsplit("/", 1)[-1].lower()
        digest = hashlib.sha256((tid+"\0"+name).encode()).hexdigest()
        if result.attrib["outcome"] != "Passed":
            raise ValueError(f'{asm}: {method.get("name")} {result.get("outcome")}')
        yield asm, name, method.attrib["className"], method.attrib["name"], digest


def reconcile(directory, output):
    moved = move_map()
    current = {}
    by_name = {}
    for file in directory.glob("*.trx"):
        for asm, name, cls, method, digest in records(ET.parse(file)):
            if digest in current:
                raise ValueError("Duplicate current test: " + method)
            current[digest] = (asm, name, cls, method)
            by_name.setdefault((asm, name), []).append(digest)
    rows = []
    with ZipFile(BASE / "trx.zip") as archive:
        for trx in archive.namelist():
            with archive.open(trx) as source:
                for asm, name, cls, method, old_id in records(ET.parse(source)):
                    old, new = moved.get(cls, (asm, asm))
                    if asm != old:
                        raise ValueError("Wrong declared source assembly: " + cls)
                    matches = [old_id] if old == new else by_name.get((new, name), [])
                    if len(matches) != 1 or current.get(matches[0]) != (new, name, cls, method):
                        raise ValueError("Changed test identity without a move: " + name)
                    rows.append((old_id, matches[0], old, new, cls+"."+method))
    frozen = set((BASE / "tests-vstest.tsv").read_text().splitlines()[1:])
    if len(rows) != 400 or len({r[1] for r in rows}) != len(rows) or {r[0] for r in rows} != frozen:
        raise ValueError("Signed reference cases are not mapped bijectively")
    with output.open("w", newline="") as f:
        writer = csv.writer(f, delimiter="\t", lineterminator="\n")
        writer.writerow(["reference_sha256", "executed_sha256", "reference_assembly", "executed_assembly", "method"])
        writer.writerows(sorted(rows))
    return len(current)

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
        classes = {r["class"]: (r["old"], r["new"]) for r in csv.DictReader(f, delimiter="\t")}
    with (DOC / "TEST-METHODES.tsv").open() as f:
        methods = {(r["class"], r["method"]): (r["old"], r["new"]) for r in csv.DictReader(f, delimiter="\t")}
    def resolve(cls, method, assembly):
        return methods.get((cls, method), classes.get(cls, (assembly, assembly)))
    return resolve


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
    with (DOC / "TEST-PARAMETRES.tsv").open() as f:
        # xUnit truncates long argument values in the TRX case name. These pairs use
        # the complete arguments observed in the adapter's serialized test cases.
        parameter_moves = {row["reference_sha256"]: row["executed_sha256"] for row in csv.DictReader(f, delimiter="\t")}
    used_parameters = set()
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
                    old, new = moved(cls, method, asm)
                    if asm != old:
                        raise ValueError("Wrong declared source assembly: " + cls)
                    matches = [old_id] if old == new else by_name.get((new, name), [])
                    if len(matches) > 1 and old_id in parameter_moves:
                        matches = [value for value in matches if value == parameter_moves[old_id]]
                        used_parameters.add(old_id)
                    if len(matches) != 1 or current.get(matches[0]) != (new, name, cls, method):
                        raise ValueError("Changed test identity without a move: " + name)
                    rows.append((old_id, matches[0], old, new, cls+"."+method))
    if used_parameters != set(parameter_moves):
        raise ValueError("Ambiguous parameter correspondence is obsolete or incomplete")
    frozen = set((BASE / "tests-vstest.tsv").read_text().splitlines()[1:])
    if len(rows) != 400 or len({r[1] for r in rows}) != len(rows) or {r[0] for r in rows} != frozen:
        raise ValueError("Signed reference cases are not mapped bijectively")
    with (DOC / "TESTS-ADDITIONNELS.tsv").open() as f:
        for row in csv.DictReader(f, delimiter="\t"):
            if current.get(row["identité a898c608"], ())[:2] != (row["assembly"], row["cas"]):
                raise ValueError("Missing case introduced during P0–P8: " + row["cas"])
    with output.open("w", newline="") as f:
        writer = csv.writer(f, delimiter="\t", lineterminator="\n")
        writer.writerow(["reference_sha256", "executed_sha256", "reference_assembly", "executed_assembly", "method"])
        writer.writerows(sorted(rows))
    return len(current)


def compare_extractions(trx_archive, extraction):
    import json
    manifest = json.loads((ROOT / "modules.lock.json").read_text())
    assemblies = {Path(p).stem.lower() + ".dll": domain
                  for domain, config in manifest["modules"].items() for p in config["projects"] if p.startswith("tests/")}
    expected = {domain: set() for domain in manifest["modules"]}
    with ZipFile(trx_archive) as archive:
        for name in archive.namelist():
            with archive.open(name) as source:
                for asm, _, _, _, digest in records(ET.parse(source)):
                    if asm in assemblies:
                        expected[assemblies[asm]].add(digest)
    for domain, identities in sorted(expected.items()):
        actual = (extraction / (domain + ".cases")).read_text().splitlines()
        if not identities or len(actual) != len(identities) or set(actual) != identities:
            raise ValueError("Cas du module différents entre extraction et maître : " + domain)
        print(f"{domain}\t{len(actual)}")


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="Comparer les identités VSTest des douze extractions et du maître.")
    parser.add_argument("trx_zip", type=Path)
    parser.add_argument("extractions", type=Path)
    args = parser.parse_args()
    compare_extractions(args.trx_zip, args.extractions)

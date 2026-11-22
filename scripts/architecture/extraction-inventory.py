#!/usr/bin/env python3
"""Export current ownership and the signed source-to-destination map from Git and Roslyn."""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

from layout import layer, projects

ROOT = Path(__file__).resolve().parents[2]
BASE = "fadd9aa41148ac3f147b25a73fb6d023928242c6"
DOC = ROOT / "docs/architecture/preparation-multidepots"


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT)


def table(name, headers, data):
    with (DOC / name).open("w", newline="") as stream:
        writer = csv.writer(stream, delimiter="\t", lineterminator="\n")
        writer.writerow(headers)
        writer.writerows(sorted(data))


def owner(path):
    return path.parts[1] if path.parts[0] == "modules" else "master"


def assembly_of(path, parents):
    return next((parents[p] for p in path.parents if p in parents), "—")


def export(capture, ports_file):
    ps = projects(ROOT) + [ROOT/"scripts/architecture/capture/Inventory.csproj"]
    parents = {p.parent.relative_to(ROOT): p.stem for p in ps}
    by_id = {p.stem: p.parent.relative_to(ROOT) for p in ps}
    tracked = {Path(p) for p in git("ls-files", "--cached", "--others", "--exclude-standard", "-z").decode().split("\0") if p and (ROOT/p).is_file()}
    captured = [l.split("\t") for f in capture.glob("*.tsv") for l in f.read_text().splitlines()]
    definitions = {r[5][2:]: Path(r[1]) for r in captured if r[0] == "D" and r[3] == "NamedType" and "/obj/" not in r[1]}
    types = [(r[5], r[3], r[4], r[1], r[2], assembly_of(Path(r[1]), parents), owner(Path(r[1])))
             for r in captured if r[0] == "D" and r[3] == "NamedType" and r[4] == "Public" and "/obj/" not in r[1]]
    table("TYPES.tsv", ["identifiant Roslyn", "nature", "visibilité", "fichier", "ligne", "assembly", "dépôt"], types)
    graph = set()
    for p in ps:
        for tag in ("ProjectReference", "PackageReference"):
            for ref in ET.parse(p).iter(tag):
                dep = ref.attrib["Include"].replace("\\", "/")
                graph.add((tag, p.stem, Path(dep).stem if tag == "ProjectReference" else dep, ref.get("Version", "—")))
        assets = json.loads((p.parent/"obj/project.assets.json").read_text(encoding="utf-8-sig"))
        for key, data in assets["libraries"].items():
            name, version = key.rsplit("/", 1)
            graph.add(("NuGet/"+data["type"], p.stem, name, version))
    graph.update(("assembly Roslyn", Path(r[1]).stem, r[2], "—") for r in captured if r[0] == "B")
    table("DEPENDENCIES.tsv", ["nature", "source", "cible", "version"], graph)
    with ports_file.open() as source:
        reader = csv.DictReader(source, delimiter="\t")
        headers = reader.fieldnames
        port_rows = []
        for row in reader:
            mocks = row["doubles"].split(" | ") if row["doubles"] else []
            destinations = [definitions[k].as_posix() for k in mocks if k in definitions]
            if len(destinations) != len(mocks):
                raise ValueError("unowned mock: " + row["port"])
            port_rows.append([row[h] for h in headers]+[" | ".join(sorted(destinations)) or "—"])
    table("PORTS.tsv", headers+["sources physiques des doubles"], port_rows)
    baseline = [entry.split("\t", 1) for entry in git("ls-tree", "-r", BASE).decode().splitlines()]
    by_filename = {}
    for p in tracked:
        by_filename.setdefault(p.name, []).append(p)
    public_doubles = {k.rsplit(".", 1)[-1]: p for k, p in definitions.items() if layer(p) == "testing"}
    moves, old_projects = [], []
    for metadata, filename in baseline:
        path = Path(filename)
        old_asm = (path.parts[1] if len(path.parts) > 2 and path.parts[0] in {"src", "tests", "benchmarks"}
                   else "Inventory" if filename.startswith("scripts/architecture/capture/") else "—")
        if path in tracked:
            found = [path]
        elif path.name == "Alycia.Modules.Testing.csproj":
            found = [d/(n+".csproj") for n, d in by_id.items() if n.endswith(".Mocks")]
        elif old_asm == "Alycia.Modules.Testing" and path.suffix == ".cs":
            code = git("show", BASE+":"+filename).decode("utf-8-sig")
            names = re.findall(r"^public (?:sealed )?(?:class|record) (\w+)", code, re.M)
            found = sorted({public_doubles[n] for n in names})
            if "SnapshotTurnReads" in names:
                found.append(public_doubles["SnapshotTurnDocuments"])
        elif old_asm in by_id and (candidate := by_id[old_asm]/Path(*path.parts[path.parts.index(old_asm)+1:])) in tracked:
            found = [candidate]
        else:
            found = by_filename.get(path.name, [])
            if len(found) != 1:
                raise ValueError(f"unresolved source destination: {filename} -> {found}")
        moves.append((filename, metadata.split()[-1], old_asm, " | ".join(str(p) for p in found),
                      " | ".join(sorted({owner(p) for p in found}))))
        if path.suffix == ".csproj":
            project = ET.fromstring(git("show", BASE+":"+filename))
            old_projects.append((filename, " | ".join(t.get("Include") for t in project.iter("ProjectReference")) or "—",
                                 " | ".join(t.get("Include")+" "+t.get("Version", "") for t in project.iter("PackageReference")) or "—"))
    table("DEPLACEMENTS.tsv", ["chemin signé", "Git blob signé", "assembly initiale", "destination", "dépôt cible"], moves)
    table("BASELINE-PROJETS.tsv", ["projet signé", "références projet", "packages"], old_projects)
    migrations = []
    for line in (ROOT/"scripts/architecture/historical-migrations.tsv").read_text().splitlines()[1:]:
        old, reference = line.split("\t")
        new = next(r[3] for r in moves if r[0] == old)
        data = (ROOT/new).read_bytes()
        if data != git("show", BASE+":"+old) or hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest() != reference:
            raise ValueError("historical migration differs: " + new)
        migrations.append((old,new,owner(Path(new)),reference))
    table("MIGRATIONS.tsv", ["origine", "fichier actuel", "dépôt", "sha256 LF"], migrations)
    ownership = DOC/"OWNERSHIP.tsv"
    tracked.add(ownership.relative_to(ROOT))
    table("OWNERSHIP.tsv", ["fichier", "assembly", "propriétaire physique", "destination Git"], [
        (p.as_posix(), assembly_of(p, parents), owner(p), "alycia" if owner(p) == "master" else "alycia-"+owner(p)) for p in tracked])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("ports", type=Path)
    args = parser.parse_args()
    export(args.capture, args.ports)

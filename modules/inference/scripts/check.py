#!/usr/bin/env python3
"""Build the current exported solution with only its sources and a versioned local feed."""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def dotnet(*args):
    command = ["dotnet", *map(str, args)]
    print(subprocess.list2cmdline(command), flush=True)
    subprocess.run(command, cwd=ROOT, check=True)


def verify_assets(project, feed):
    assets = json.loads((project.parent / "obj/project.assets.json").read_text(encoding="utf-8-sig"))
    config = assets["project"]["restore"]
    locks = json.loads(project.with_name("packages.lock.json").read_text())["dependencies"]
    allowed_binaries = set()
    allowed_sources = set()
    for tfm, target in assets["targets"].items():
        identities = set()
        for key, item in target.items():
            name, version = key.rsplit("/", 1)
            if name.casefold() in identities:
                raise ValueError(f"ambiguous assembly/package {name} in {project.name}")
            identities.add(name.casefold())
            lib = assets["libraries"][key]
            if item["type"] == "project":
                ref = (project.parent / lib["msbuildProject"]).resolve()
                if not ref.is_file() or not ref.is_relative_to(ROOT):
                    raise ValueError(f"external project: {ref}")
            else:
                if not name.startswith("Alycia."):
                    for folder in assets["packageFolders"]:
                        allowed_sources.update((Path(folder) / lib["path"] / f).resolve()
                                               for f in lib.get("files", []) if f.endswith(".cs"))
                entry = locks[tfm.split("/")[0]][name]
                if (entry["resolved"], entry["contentHash"]) != (version, lib["sha512"]):
                    raise ValueError(f"unlocked resolved package: {key}")
                if name.startswith("Alycia."):
                    for folder in assets["packageFolders"]:
                        for binary in item.get("compile", {}):
                            allowed_binaries.add((Path(folder) / lib["path"] / binary).resolve())
                    archive = feed / f"{name}.{version}.nupkg"
                    actual = base64.b64encode(hashlib.sha512(archive.read_bytes()).digest()).decode()
                    if actual != lib["sha512"]:
                        raise ValueError(f"cached package differs from supplied feed: {key}")
        for dep in config["frameworks"][tfm.split("/")[0]]["projectReferences"]:
            if not Path(dep).resolve().is_relative_to(ROOT):
                raise ValueError(f"external MSBuild project: {dep}")
    # MSBuild expands conditional references and linked sources here. Reject escaped symlinks too.
    inspection = subprocess.run(["dotnet", "msbuild", str(project), "-nologo", "-m:1", "-p:Configuration=Release", "-p:BuildProjectReferences=false",
                                     "-target:ResolveReferences", "-getProperty:AssemblyName", "-getItem:Compile,ProjectReference,ReferencePath"], cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if inspection.returncode:
        raise RuntimeError(f"MSBuild inspection of {project} failed: {inspection.stdout}")
    evaluated = json.loads(inspection.stdout)
    if evaluated["Properties"]["AssemblyName"] != project.stem:
        raise ValueError(f"unexpected assembly identity: {project.stem}")
    identities = set()
    for reference in evaluated["Items"].pop("ReferencePath"):
        resolved = Path(reference["FullPath"]).resolve()
        identity = reference.get("FusionName", "").split(",")[0] or resolved.stem
        if not identity.casefold().startswith("alycia."):
            continue
        if identity.casefold() in identities or (not resolved.is_relative_to(ROOT) and resolved not in allowed_binaries):
            raise ValueError(f"ambiguous or external compiled assembly {identity}: {resolved}")
        identities.add(identity.casefold())
    for kind, entries in evaluated["Items"].items():
        for entry in entries:
            path = Path(entry["FullPath"]).resolve()
            if not path.is_relative_to(ROOT) and not (kind == "Compile" and path in allowed_sources):
                raise ValueError(f"external source: {entry['FullPath']}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("build", "test", "check"), nargs="?", default="check")
    parser.add_argument("--feed", type=Path, default=ROOT / "artifacts/local-feed")
    parser.add_argument("--cases-out", type=Path, help="Écrire les identités des cas VSTest réussis")
    args = parser.parse_args()
    if not shutil.which("dotnet"):
        parser.error("SDK .NET 10 requis")
    feed = args.feed.resolve()
    (solution,) = ROOT.glob("*.slnx")
    projects = [ROOT / p.attrib["Path"] for p in ET.parse(solution).iter("Project")]
    if any(not p.is_file() or not p.resolve().is_relative_to(ROOT) or not p.with_name("packages.lock.json").is_file() for p in projects):
        parser.error("Projet ou verrou absent du module")
    with tempfile.TemporaryDirectory(prefix="alycia-module-run-") as tmp:
        work = Path(tmp)
        config = ET.parse(ROOT / "NuGet.Config")
        config.find(".//packageSources/add[@key='alycia']").set("value", str(feed))
        config.write(work / "NuGet.Config", encoding="utf-8", xml_declaration=True)
        dotnet("restore", solution, "--locked-mode", "--configfile", work / "NuGet.Config", "-m:1")
        dotnet("build", solution, "-c", "Release", "--no-restore", "-m:1", "-v:q")
        for project in projects:
            verify_assets(project, feed)
        if args.action != "build":
            dotnet("test", solution, "-c", "Release", "--no-build", "--no-restore", "-m:1", "--logger", "trx", "--results-directory", work)
            cases = [c for trx in work.glob("*.trx") for c in ET.parse(trx).findall(".//{*}UnitTestResult")]
            if not cases or any(c.get("outcome") != "Passed" for c in cases):
                raise RuntimeError("suite absente, en échec ou ignorée ; fournir les prérequis de la recette")
            if args.cases_out:
                ids = sorted(hashlib.sha256((c.attrib["testId"] + "\0" + c.attrib["testName"]).encode()).hexdigest() for c in cases)
                if len(ids) != len(set(ids)):
                    raise ValueError("identité VSTest dupliquée")
                args.cases_out.write_text("\n".join(ids) + "\n")
            print(f"PASS {solution.stem}: {len(projects)} projects, {len(cases)} passed, 0 skipped", flush=True)


if __name__ == "__main__":
    main()

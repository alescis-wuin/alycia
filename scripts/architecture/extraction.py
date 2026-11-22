"""Check the manifest against physical solutions, project references and NuGet locks."""
import csv
import json
import re
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path

from .layout import layer, projects

DOMAINS = set("contracts conversations workspaces documents indexing retrieval inference context memory tools configuration platform".split())
SHARED = {"Alycia.Contracts", "Alycia.Platform.Persistence"}


def solution_projects(solution):
    return [(solution.parent / p.attrib["Path"].replace("\\", "/")).resolve() for p in ET.parse(solution).iter("Project")]


def validate_layout(root):
    problems = []
    try:
        manifest = json.loads((root / "modules.lock.json").read_text())
    except (OSError, ValueError) as exc:
        return [f"module manifest: {exc}"]
    modules = manifest["modules"]
    dirs = {p.name for p in (root / "modules").iterdir() if p.is_dir()}
    if set(modules) != DOMAINS or dirs != DOMAINS:
        problems.append(f"missing or unexpected modules: {sorted(DOMAINS ^ dirs | DOMAINS ^ set(modules))}")
    declared_tools = {root / p for p in manifest["tooling"]}
    actual_tools = {p for p in (root / "scripts").rglob("*.csproj") if not {"bin", "obj"} & set(p.parts)}
    if actual_tools != declared_tools or any(not p.is_file() for p in declared_tools):
        problems.append("missing or unexpected build tool")
    all_projects = projects(root)
    by_id = Counter()
    declared = set()
    for name, entry in modules.items():
        base = root / "modules" / name
        expected = {base / p for p in entry["projects"]}
        sol = base / entry["solution"]
        if entry["directory"] != str(base.relative_to(root).as_posix()) or not sol.is_file():
            problems.append(f"module solution missing: {name}")
            continue
        listed = solution_projects(sol)
        if set(listed) != expected or len(listed) != len(expected) or any(not p.is_relative_to(base) for p in listed):
            problems.append(f"module project inventory: {name}")
        declared.update(expected)
        actual_packages = set()
        for p in expected:
            if not p.is_file():
                problems.append(f"missing project: {p}")
                continue
            doc = ET.parse(p)
            for ref in doc.iter("ProjectReference"):
                target = (p.parent / ref.attrib["Include"].replace("\\", "/")).resolve()
                if not target.is_file() or not target.is_relative_to(base):
                    problems.append(f"inter-module source reference: {p.stem} -> {target.stem}")
            for package in doc.iter("PackageReference"):
                dep = package.attrib["Include"]
                if not dep.startswith("Alycia."):
                    continue
                actual_packages.add(dep)
                version = manifest["packages"].get(dep)
                if not version or package.get("Version") != f"[{version}]":
                    problems.append(f"unlocked internal package: {p.stem} -> {dep}")
                if dep not in SHARED and not (layer(p.relative_to(root)) == "tests" and dep.endswith(".Mocks")):
                    problems.append(f"prohibited package: {p.stem} -> {dep}")
                if name == "contracts" or (name == "platform" and dep not in {"Alycia.Contracts"}):
                    problems.append(f"shared package depends on domain: {p.stem} -> {dep}")
        if set(entry["packages"]) != actual_packages:
            problems.append(f"module package inventory: {name}")
    master_paths = {root / p for p in manifest["master"]}
    entry_points = [root / p for p in manifest["productionEntryPoints"] + [manifest["developmentEntryPoint"]]]
    if len(set(entry_points)) != 3 or not set(entry_points).issubset(master_paths):
        problems.append("master entry points are missing or ambiguous")
    declared.update(master_paths)
    if declared != set(all_projects) or set(solution_projects(root / "Alycia.slnx")) != declared:
        problems.append("master project inventory differs from physical projects or manifest")
    for p in all_projects:
        doc = ET.parse(p)
        identity = doc.findtext(".//AssemblyName") or p.stem
        by_id[identity.casefold()] += 1
        lock = p.with_name("packages.lock.json")
        if not lock.is_file():
            problems.append(f"missing package lock: {p.stem}")
            continue
        locked = json.loads(lock.read_text())["dependencies"]
        for group in locked.values():
            for dep, data in group.items():
                if p.stem == "Alycia.Api" and (dep.casefold().endswith((".mocks", "fixtures", ".tests")) or dep.casefold() == "alycia.modules.testing"):
                    problems.append(f"test dependency in production: {dep}")
                if data["type"].lower() == "project":
                    continue
                if not data.get("resolved") or not data.get("contentHash"):
                    problems.append(f"incomplete package lock: {p.stem} -> {dep}")
                if dep.startswith("Alycia.") and data.get("resolved") != manifest["packages"].get(dep):
                    problems.append(f"unregistered package closure: {p.stem} -> {dep}")
        for ref in doc.iter("ProjectReference"):
            name = Path(ref.attrib["Include"].replace("\\", "/")).stem
            if name in SHARED and not p.is_relative_to(root / "modules" / ("contracts" if name == "Alycia.Contracts" else "platform")):
                problems.append(f"shared assembly resolved from source: {p.stem} -> {name}")
        for pkg in doc.iter("PackageReference"):
            version = pkg.get("Version", "")
            if not re.fullmatch(r"(?:\[)?\d[\w.\-]*(?:\])?", version):
                problems.append(f"unlocked package declaration: {p.stem} -> {pkg.get('Include')}")
            if not any(g.get(pkg.get("Include"), {}).get("type") == "Direct" for g in locked.values()):
                problems.append(f"missing direct package in lock: {p.stem} -> {pkg.get('Include')}")
    problems.extend(f"duplicate assembly identity: {key}" for key, count in by_id.items() if count > 1)
    return problems

def port_errors(root, ports):
    with (root / "scripts/architecture/port-owners.tsv").open() as stream:
        entries = list(csv.DictReader(stream, delimiter="\t"))
    owners = {e["port"]: e["owner"] for e in entries}
    errors = [f"port without unique owner: {key}" for key in sorted(set(owners) ^ ports)]
    if len(owners) != len(entries) or set(owners.values()) - DOMAINS:
        errors.append("invalid or duplicate port owner")
    return errors

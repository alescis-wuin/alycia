#!/usr/bin/env python3
"""Verify project edges and compiler-resolved product code boundaries."""
import argparse
from collections import defaultdict
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

from architecture.module_rules import ADAPTERS, ALLOWED, CHAT_PROJECTS, NARROW_EDGES, owner

ROOT = Path(__file__).resolve().parents[1]
SCANNER = ROOT / "scripts/architecture/Symbols.csproj"
MODULE_NAMESPACE = re.compile(r"^Alycia\.(?:Api|Client\.Avalonia|Modules\.\w+\.(?:Domain|Application|Infrastructure))\b")
PROVIDER = re.compile(r"^(?:Microsoft\.EntityFrameworkCore|Npgsql|Microsoft\.ML\.OnnxRuntime)(?:\.|$)")


def forbidden(project, dependency):
    if project == "Alycia.Contracts":
        return dependency.startswith(("Alycia.Modules.", "Alycia.Api", "Alycia.Client."))
    if project == "Alycia.Client.Avalonia":
        return dependency.startswith(("Alycia.Modules.", "Alycia.Api"))
    if project.startswith("Alycia.Modules.") and project.endswith((".Domain", ".Application", ".Infrastructure")):
        return (dependency in {"Alycia.Api", "Alycia.Client.Avalonia"}
                or project.endswith(".Domain") and dependency.endswith((".Application", ".Infrastructure"))
                or project.endswith(".Application") and dependency.endswith(".Infrastructure"))
    return False


def check_project_graph(root):
    paths = sorted(p for group in ("src", "tests", "benchmarks") for p in (root / group).glob("*/*.csproj"))
    graph = {p: [] for p in paths}
    errors = []
    listed = {(root / p.get("Path").replace("\\", "/")).resolve() for p in ET.parse(root / "Alycia.slnx").iter("Project")}
    required = {p for p in graph if p.relative_to(root).parts[0] in {"src", "tests"}}
    for missing in sorted(required - listed):
        errors.append(f"not in solution: {missing.relative_to(root)}")
    for missing in sorted(listed - graph.keys()):
        errors.append(f"unknown solution project: {missing.relative_to(root)}")
    for project in paths:
        doc = ET.parse(project)
        for use in doc.iter("Using"):
            errors += check_namespace(project.stem, str(project.relative_to(root)), use.get("Include", ""))
        for ref in doc.iter("ProjectReference"):
            target = (project.parent / ref.attrib["Include"].replace("\\", "/")).resolve()
            graph[project].append(target)
            if target not in graph:
                errors.append(f"{project.stem}: unknown project {target.name}")
            elif project.stem == "Alycia.Contracts" or forbidden(project.stem, target.stem) or (
                    project.parent.parent.name == "src" and target.parent.parent.name != "src"):
                errors.append(f"project layer: {project.stem} -> {target.stem}")

    done = set()
    def visit(project, path):
        if project in path:
            cycle = path[path.index(project):] + [project]
            errors.append("cycle: " + " -> ".join(p.stem for p in cycle))
            return
        if project in done or project not in graph:
            return
        for dependency in graph[project]:
            visit(dependency, path + [project])
        done.add(project)

    for project in graph:
        visit(project, [])
        if project.stem == "Alycia.Client.Avalonia":
            stack, seen = list(graph[project]), set()
            while stack:
                target = stack.pop()
                if target in seen:
                    continue
                seen.add(target)
                if target.stem != "Alycia.Contracts":
                    errors.append(f"client boundary: {project.stem} transitively reaches {target.stem}")
                stack.extend(graph.get(target, ()))
    return len(graph), errors


def business(project):
    return project == "Alycia.Contracts" or project.endswith((".Application", ".Domain"))


def check_namespace(project, location, name):
    match = MODULE_NAMESPACE.match(name)
    if match and forbidden(project, match.group()):
        return [f"{location}: project layer: {project} -> {match.group()}"]
    if business(project) and PROVIDER.match(name):
        return [f"{location}: business layer imports technical provider {name}"]
    return []


def inspect_symbols(root):
    proc = subprocess.run(["dotnet", "run", "--project", str(SCANNER), "--no-launch-profile", "--", str(root)],
                          capture_output=True, text=True)
    if proc.returncode:
        raise RuntimeError(proc.stderr + proc.stdout)
    problems = set()
    matrix = defaultdict(set)
    for row in proc.stdout.splitlines():
        fields = row.split("\t")
        if len(fields) < 2:  # SDK build messages
            continue
        kind, path = fields[:2]
        project = Path(path).parts[1]
        if kind == "F":
            if project in CHAT_PROJECTS and owner(path) is None:
                problems.add(f"{path}: missing logical owner (use a feature directory or register the file)")
            continue
        location = f"{path}:{fields[2]}"
        if kind == "N":
            problems.update(check_namespace(project, location, fields[3]))
        elif kind == "T" and business(project):
            problems.add(f"{location}: business port exposes technical type {fields[3]}")
        elif kind == "P" and project == "Alycia.Api":
            problems.add(f"{location}: endpoint persistence belongs in the database adapter ({fields[3]})")
        elif kind == "R":
            source_type, target, target_type = fields[3:]
            target_project = Path(target).parts[1]
            if forbidden(project, target_project):
                problems.add(f"{location}: project layer: {project} -> {target_project}")
            left, right = owner(path, source_type), owner(target, target_type)
            if not left or not right or left == right:
                continue
            matrix[left, right].add(target_type)
            if right not in ALLOWED[left]:
                problems.add(f"{location}: logical boundary: {left} -> {right} ({target_type})")
            elif (left, right) in NARROW_EDGES and target_type not in NARROW_EDGES[left, right]:
                problems.add(f"{location}: narrow boundary: {left} -> {right}.{target_type}")
            elif target_project == "Alycia.Modules.Chat.Infrastructure" and not (
                    right == "Persistence" and target_type == "ChatDbContext" or target_type in ADAPTERS.get(left, ())):
                problems.add(f"{location}: cross-owner adapter: {left} -> {right}.{target_type}")
    return sorted(problems), matrix


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT, help="source tree to inspect")
    parser.add_argument("--matrix", action="store_true", help="print resolved cross-owner types as TSV")
    args = parser.parse_args()
    root = args.root.resolve()
    count, problems = check_project_graph(root)
    if not problems:
        try:
            problems, matrix = inspect_symbols(root)
        except (OSError, RuntimeError) as error:
            sys.exit(f"architecture scanner: {error}")
        if args.matrix:
            for (left, right), types in sorted(matrix.items()):
                print("\t".join((left, right, ", ".join(sorted(types)))))
    if problems:
        sys.exit("\n".join(problems))
    if not args.matrix:
        print(f"Architecture OK ({count} projects; {len(ALLOWED)} logical owners).")

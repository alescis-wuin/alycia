#!/usr/bin/env python3
"""Verify project edges and compiler-resolved product code boundaries."""
import argparse
from collections import defaultdict
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

from architecture.module_rules import ADAPTERS, ALLOWED, CHAT_PROJECTS, CONTRACT_AREAS, NARROW_EDGES, owner

ROOT = Path(__file__).resolve().parents[1]
CAPTURE = ROOT / "scripts/architecture/capture"
MODULE_NAMESPACE = re.compile(r"^Alycia\.(?:Api|Client\.Avalonia|Modules\.\w+\.(?:Domain|Application|Infrastructure))\b")
PROVIDER = re.compile(r"^(?:Microsoft\.EntityFrameworkCore|Npgsql|Microsoft\.ML\.OnnxRuntime)(?:\.|$)")


ISOLATED = {
    "Alycia.Contracts": set(),
    "Alycia.Modules.Documents": {"Alycia.Contracts"},
    "Alycia.Modules.Inference": {"Alycia.Contracts"},
    "Alycia.Modules.Indexing": {"Alycia.Contracts"},
    "Alycia.Modules.Tools": {"Alycia.Contracts"},
    "Alycia.Modules.Context": {"Alycia.Contracts"},
    "Alycia.Modules.Memory": {"Alycia.Contracts"},
    "Alycia.Modules.Retrieval": {"Alycia.Contracts"},
    "Alycia.Modules.Retrieval.Onnx": {"Alycia.Contracts"},
    "Alycia.Modules.Testing": {"Alycia.Contracts"},
    "Alycia.Platform.Persistence": set(),
    "Alycia.Modules.Chat.Domain": set(),
    "Alycia.Modules.Workspaces.Domain": set(),
    "Alycia.Modules.Workspaces.Application": {"Alycia.Modules.Workspaces.Domain"},
    "Alycia.Modules.Workspaces.Infrastructure": {"Alycia.Contracts", "Alycia.Platform.Persistence",
        "Alycia.Modules.Workspaces.Domain", "Alycia.Modules.Workspaces.Application"},
}


def forbidden(project, dependency):
    if project in ISOLATED and dependency.startswith("Alycia.") and dependency != project:
        return dependency not in ISOLATED[project]

    if project == "Alycia.Modules.Chat.Infrastructure" and dependency.startswith("Alycia.Modules.Workspaces."):
        return True
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
    return project in {"Alycia.Contracts", "Alycia.Modules.Retrieval", "Alycia.Modules.Testing"} or project.endswith((".Application", ".Domain"))


def check_namespace(project, location, name):
    match = MODULE_NAMESPACE.match(name)
    if match and forbidden(project, match.group()):
        return [f"{location}: project layer: {project} -> {match.group()}"]
    if business(project) and PROVIDER.match(name):
        return [f"{location}: business layer imports technical provider {name}"]
    return []


TABLE_OWNERS = {
    "workspaces.workspaces": "Workspaces", "chat.workspace_profiles": "Workspaces",
    "chat.workspace_file_overrides": "Documents", "chat.attachments": "Documents", "chat.document_analyses": "Documents",
    "chat.conversations": "Conversations", "chat.messages": "Conversations",
    "chat.embedding_jobs": "Indexing", "chat.embedding_vectors": "Indexing", "chat.knowledge_units": "Indexing",
    "chat.temporal_memory": "Memory", "chat.prompt_profiles": "Configuration", "chat.import_settings": "Configuration",
}
SETS = dict(WorkspaceProfiles="chat.workspace_profiles", WorkspaceFileOverrides="chat.workspace_file_overrides",
            Attachments="chat.attachments", DocumentAnalyses="chat.document_analyses", Conversations="chat.conversations",
            Messages="chat.messages", PromptProfiles="chat.prompt_profiles", Workspaces="workspaces.workspaces")


def capture(root, directory):
    project = CAPTURE / "Inventory.csproj"
    def build(*arguments):
        proc = subprocess.run(["dotnet", "build", *map(str, arguments), "-m:1", "-v:q", "-p:UseSharedCompilation=false"],
                              cwd=root, capture_output=True, text=True)
        if proc.returncode:
            raise RuntimeError(proc.stdout + proc.stderr)
    build(project)
    projects = sorted((root / "src").glob("*/*.csproj"))
    if not projects:
        raise ValueError("no product projects found")
    solution = ET.Element("Solution")
    for file in projects:
        ET.SubElement(solution, "Project", Path=file.as_posix())
    entry = directory / "Product.slnx"
    ET.ElementTree(solution).write(entry)
    build(entry, "-t:Rebuild", "-warnaserror:AD0001", "-p:RunAnalyzers=true",
          f"-p:CustomAfterMicrosoftCommonTargets={CAPTURE / 'Capture.targets'}",
          f"-p:AlyciaArchitectureAnalyzer={CAPTURE / 'bin/Debug/net10.0/Inventory.dll'}",
          f"-p:AlyciaArchitectureRoot={root}", f"-p:AlyciaArchitectureOutput={directory}")
    rows = [line.split("\t") for file in sorted(directory.glob("*.tsv")) for line in file.read_text().splitlines()]
    compiled = {root / path for kind, path, *_ in rows if kind == "A"}
    if set(projects) != compiled:
        raise RuntimeError(f"missing/unexpected compiler output: {sorted(set(projects) ^ compiled)}")
    return rows


def inspect_symbols(root):
    with tempfile.TemporaryDirectory() as temp:
        directory = Path(temp)
        rows = capture(root, directory)
        project_paths = {row[2]: root / row[1] for row in rows if row[0] == "A"}
        evaluated = {p: [Path(ref).resolve() for ref in (directory / (name + ".refs")).read_text(encoding="utf-8-sig").splitlines()]
                     for name, p in project_paths.items()}
    problems, matrix = check_records(rows, root)
    for project in evaluated:
        visited = set()
        stack = list(evaluated[project])
        while stack:
            target = stack.pop()
            if target in visited:
                continue
            visited.add(target)
            if target == project or forbidden(project.stem, target.stem) or target not in evaluated or (
                    project.stem == "Alycia.Client.Avalonia" and target.stem != "Alycia.Contracts"):
                problems.append(f"evaluated project boundary: {project.stem} -> {target.stem}")
            stack.extend(evaluated.get(target, ()))
    return sorted(set(problems)), matrix


def check_records(rows, root):
    declared = {(r[1].split("/")[1], r[5]): r[1] for r in rows if r[0] == "D" and r[3] == "NamedType"
                and r[1].startswith("src/") and "/obj/" not in r[1]}
    reviewed = root / "scripts/architecture/transition.tsv"
    exceptions = set()
    if reviewed.exists():
        for line in reviewed.read_text().splitlines()[1:]:
            path, kind, target, *_ = line.split("\t")
            exceptions.add((path, kind, target))
    problems = set()
    matrix = defaultdict(set)
    for fields in rows:
        if len(fields) < 2:  # SDK build messages
            continue
        kind, path = fields[:2]
        if not path.startswith("src/") or "/obj/" in path:
            continue
        project = Path(path).parts[1]
        if kind == "F":
            if project in CHAT_PROJECTS and "/Migrations/" not in path and owner(path) is None:
                problems.add(f"{path}: missing logical owner (use a feature directory or register the file)")
            continue
        if kind in {"A", "B", "D", "C", "L"}:
            continue
        location = f"{path}:{fields[2]}"
        if kind == "N":
            problems.update(check_namespace(project, location, fields[3]))
        elif kind == "T" and business(project):
            problems.add(f"{location}: business port exposes technical type {fields[3]}")
        elif kind == "P" and project == "Alycia.Api":
            problems.add(f"{location}: endpoint persistence belongs in the database adapter ({fields[3]})")
        elif kind in ("S", "E", "Y") and "/Migrations/" not in path:
            resource = SETS.get(fields[4], fields[4]) if kind == "E" else fields[3]
            area = ({"Alycia.Modules.Indexing": "Indexing", "Alycia.Modules.Memory": "Memory"}.get(project) or owner(path)) or ("Workspaces" if ".Workspaces." in project else project)
            if kind != "Y" and (area == TABLE_OWNERS.get(resource)):
                continue
            if (path, kind, resource) not in exceptions:
                problems.add(f"{location}: unreviewed {kind}: {area} -> {resource}")
        elif kind == "R":
            source_type, assembly, type_id, target_type = fields[3:]
            if assembly == "Alycia.Contracts" and project in CONTRACT_AREAS:
                area = type_id.removeprefix("T:Alycia.Contracts.").split(".", 1)[0]
                if area not in CONTRACT_AREAS[project]:
                    problems.add(f"{location}: contract boundary: {project} -> {type_id}")
            target = declared.get((assembly, type_id))
            if target is None:
                if forbidden(project, assembly):
                    problems.add(f"{location}: project layer: {project} -> {assembly}")
                continue
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
                    right == "Persistence" and target_type == "ChatDbContext" or target_type in ADAPTERS.get(left, ()) or (path, "R", right + "." + target_type) in exceptions):
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

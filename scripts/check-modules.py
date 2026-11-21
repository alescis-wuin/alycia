#!/usr/bin/env python3
"""Check product/test project edges and layer imports using only the Python standard library."""
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MODULE_NAMESPACE = re.compile(r"\bAlycia\.(?:Api|Client\.Avalonia|Modules\.\w+\.(?:Domain|Application|Infrastructure))\b")


def forbidden(owner, dependency):
    if owner == "Alycia.Contracts":
        return dependency.startswith(("Alycia.Modules.", "Alycia.Api", "Alycia.Client."))
    if owner == "Alycia.Client.Avalonia":
        return dependency.startswith(("Alycia.Modules.", "Alycia.Api"))
    if owner.startswith("Alycia.Modules.") and owner.endswith((".Domain", ".Application", ".Infrastructure")):
        return (dependency in {"Alycia.Api", "Alycia.Client.Avalonia"}
                or owner.endswith(".Domain") and dependency.endswith((".Application", ".Infrastructure"))
                or owner.endswith(".Application") and dependency.endswith(".Infrastructure"))
    return False


def check(root):
    paths = sorted(p for group in ("src", "tests", "benchmarks") for p in (root / group).glob("*/*.csproj"))
    graph = {p.resolve(): [] for p in paths}
    errors = []
    solution = root / "Alycia.slnx"
    listed = {(root / p.get("Path").replace("\\", "/")).resolve() for p in ET.parse(solution).iter("Project")}
    required = {p for p in graph if p.relative_to(root).parts[0] in {"src", "tests"}}
    for missing in sorted(required - listed):
        errors.append(f"not in solution: {missing.relative_to(root)}")
    for missing in sorted(listed - graph.keys()):
        errors.append(f"unknown solution project: {missing.relative_to(root)}")
    for project in paths:
        document = ET.parse(project)
        for namespace in document.iter("Using"):
            for name in MODULE_NAMESPACE.findall(namespace.get("Include", "")):
                if forbidden(project.stem, name):
                    errors.append(f"{project.stem} imports {name}")
        for ref in document.iter("ProjectReference"):
            target = (project.parent / ref.attrib["Include"].replace("\\", "/")).resolve()
            graph[project.resolve()].append(target)
            if target not in graph:
                errors.append(f"{project.stem}: unknown project {target.name}")
            elif forbidden(project.stem, target.stem):
                errors.append(f"{project.stem} -> {target.stem}")
        if project.parent.parent.name != "src":
            continue
        for source in project.parent.rglob("*.cs"):
            if {"bin", "obj"}.intersection(source.relative_to(project.parent).parts):
                continue
            for line_no, line in enumerate(source.read_text(encoding="utf-8-sig").splitlines(), 1):
                code = line.partition("//")[0]
                if (project.stem == "Alycia.Api" and "Endpoints" in source.stem
                        and re.search(r"\b(?:ChatDbContext|WorkspacesDbContext|ExecuteSqlRawAsync|ExecuteSqlInterpolatedAsync|SqlQueryRaw)\b", code)):
                    errors.append(f"{source.relative_to(root)}:{line_no}: EF belongs in the database adapter")
                if not re.search(r"\busing\s|\bAlycia\.\w", code):
                    continue
                for match in MODULE_NAMESPACE.finditer(code):
                    if forbidden(project.stem, match.group()):
                        errors.append(f"{source.relative_to(root)}:{line_no}: {match.group()}")

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
    for start in graph:
        if start.stem != "Alycia.Client.Avalonia":
            continue
        stack, seen = list(graph[start]), set()
        while stack:
            target = stack.pop()
            if target in seen:
                continue
            seen.add(target)
            if forbidden(start.stem, target.stem):
                errors.append(f"{start.stem} transitively reaches {target.stem}")
            stack.extend(graph.get(target, ()))
    return len(graph), errors


if __name__ == "__main__":
    count, problems = check(ROOT)
    if problems:
        print("\n".join(problems), file=sys.stderr)
        sys.exit(1)
    print(f"Architecture OK ({count} projects).")

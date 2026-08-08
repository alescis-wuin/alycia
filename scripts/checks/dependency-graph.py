#!/usr/bin/env python3
from __future__ import annotations

import sys
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "src"

ALLOWED: dict[str, set[str]] = {
    "Alicia.Domain": set(),
    "Alicia.Application": {"Alicia.Domain"},
    "Alicia.Infrastructure": {"Alicia.Application", "Alicia.Domain"},
    "Alicia.Presentation": {"Alicia.Application", "Alicia.Domain"},
    "Alicia.Desktop": {"Alicia.Presentation"},
}


def project_name(project: Path) -> str:
    return project.stem


def references(project: Path) -> set[str]:
    root = ElementTree.parse(project).getroot()
    found: set[str] = set()
    for item in root.findall(".//ProjectReference"):
        include = item.attrib.get("Include")
        if not include:
            continue
        found.add(Path(include.replace("\\", "/")).stem)
    return found


def main() -> int:
    projects = {project_name(path): path for path in sorted(SRC.glob("*/*.csproj"))}
    errors: list[str] = []

    missing = sorted(set(ALLOWED) - set(projects))
    unexpected = sorted(set(projects) - set(ALLOWED))
    if missing:
        errors.append(f"missing projects: {', '.join(missing)}")
    if unexpected:
        errors.append(f"unclassified projects: {', '.join(unexpected)}")

    graph: dict[str, set[str]] = {}
    for name, path in projects.items():
        graph[name] = references(path)
        unknown = sorted(reference for reference in graph[name] if reference not in projects)
        if unknown:
            errors.append(f"{name}: unresolved internal references: {', '.join(unknown)}")
        if name in ALLOWED and graph[name] != ALLOWED[name]:
            errors.append(
                f"{name}: expected {sorted(ALLOWED[name])}, found {sorted(graph[name])}"
            )

    visiting: set[str] = set()
    visited: set[str] = set()

    def visit(node: str, trail: list[str]) -> None:
        if node in visiting:
            errors.append("cycle: " + " -> ".join(trail + [node]))
            return
        if node in visited:
            return
        visiting.add(node)
        for child in sorted(graph.get(node, set())):
            visit(child, trail + [node])
        visiting.remove(node)
        visited.add(node)

    for node in sorted(graph):
        visit(node, [])

    for node in sorted(graph):
        targets = ", ".join(sorted(graph[node])) or "(none)"
        print(f"[GRAPH] {node} -> {targets}")

    if errors:
        for error in errors:
            print(f"[ERROR] {error}", file=sys.stderr)
        return 1

    print("[OK] Internal project dependency graph is valid and acyclic.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

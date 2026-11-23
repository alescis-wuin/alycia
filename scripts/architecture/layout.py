"""Paths in current modules and pre-extraction compiler inventories."""
from pathlib import PurePosixPath


def parts(path):
    fields = PurePosixPath(path).parts
    return fields[2:] if fields[:1] == ("modules",) else fields


def layer(path):
    fields = parts(path)
    return fields[0] if fields else ""


def assembly(path):
    return parts(path)[1]


def projects(root):
    paths = set()
    for parent in ("modules", "src", "tests", "benchmarks"):
        for path in (root / parent).rglob("*.csproj"):
            if not {"bin", "obj", "artifacts", ".git"} & set(path.relative_to(root).parts):
                paths.add(path)
    return sorted(paths)


def owned_sources(root, group):
    return (file for project in projects(root) if layer(project.relative_to(root)) == group
            for file in project.parent.rglob("*.cs") if not {"bin", "obj"} & set(file.parts))


def implementation_group(path):
    fields = parts(path)
    if layer(path) == "testing" or fields[1] == "Alycia.Modules.Testing" or (layer(path) == "tests" and "Doubles" in fields):
        return "double"
    return layer(path)

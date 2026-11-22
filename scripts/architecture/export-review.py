#!/usr/bin/env python3
"""Export the actual project graph and implementations bound to public contract types."""
import argparse
import csv
from collections import defaultdict
import importlib.util
from pathlib import Path
import sys

from layout import assembly, layer, parts as layout_parts, projects as project_paths, implementation_group, owned_sources
from module_rules import owner

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
spec = importlib.util.spec_from_file_location("architecture_guard", ROOT / "scripts/check-modules.py")
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)

OWNERS = {
    "IConversationIndexOwners": "Conversations", "IProjectIndexOwners": "Workspaces",
    "IToolConversationPolicy": "Conversations", "IGenerationSettings": "Configuration",
    "IIndexSettings": "Configuration", "IToolWindowSettings": "Configuration",
}


def export(directory, destination):
    rows = [line.split("\t") for f in sorted(directory.glob("*.tsv")) for line in f.read_text().splitlines()]
    projects = {r[2]: ROOT / r[1] for r in rows if r[0] == "A"}
    properties = {p.stem for p in project_paths(ROOT)}
    if set(projects) != properties:
        raise ValueError("Capture must include the solution and the benchmark")
    problems, logical = guard.check_records(rows, ROOT)
    if problems:
        raise ValueError("\n".join(sorted(problems)))
    destination.mkdir(parents=True, exist_ok=True)
    with (destination / "D19-graphe-final.tsv").open("w", newline="") as f:
        write = csv.writer(f, delimiter="\t", lineterminator="\n")
        write.writerow(["type", "source", "destination", "détail"])
        for project, path in sorted(projects.items()):
            write.writerow(["projet", project, "", path.relative_to(ROOT).as_posix()])
            for reference in sorted((directory / (project + ".refs")).read_text(encoding="utf-8-sig").splitlines()):
                write.writerow(["référence MSBuild", project, Path(reference).stem, "ProjectReference évaluée"])
        for pair, types in sorted(logical.items()):
            write.writerow(["relation entre propriétaires", *pair, ", ".join(sorted(types))])

    interfaces = {r[2]: r[1] for r in rows if r[0] == "I" and layer(r[1]) == "src" and "/obj/" not in r[1]}
    implementations = defaultdict(lambda: defaultdict(set))
    for row in rows:
        if row[0] == "M" and row[3] in interfaces and "/obj/" not in row[1]:
            path = Path(row[1])
            group = implementation_group(path)
            implementations[row[3]][group].add(row[2])
    with (destination / "D19-matrice-contrats.csv").open("w", newline="") as f:
        write = csv.writer(f, lineterminator="\n")
        write.writerow(["propriétaire", "port", "déclaration", "implémentations produit", "doubles réutilisables",
                        "implémentations dans les tests", "conformité commune adaptateur et double"])
        for port, path in sorted(interfaces.items()):
            name = port.rsplit(".", 1)[-1]
            parts = layout_parts(path)
            area = OWNERS.get(name) or owner(path, name) or ("Workspaces" if "Workspaces" in parts[1] else parts[2])
            groups = implementations[port]
            suite = ("TextGenerationContractTests.Text_and_stream_agree_and_enforce_capabilities"
                     if name == "ITextGeneration" else "TextGenerationContractTests.Vector_provider_preserves_row_order_identity_and_cancellation"
                     if name == "ILocalEmbeddings" else "non démontrée")
            write.writerow([area, port, path, *[" | ".join(sorted(groups[k])) for k in ("src", "double", "tests")], suite])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("compiler_directory", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    export(args.compiler_directory, args.destination)

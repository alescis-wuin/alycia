"""Reproduce the D01 inventory from the compiler collector's TSVs."""
import argparse
import csv
from pathlib import Path

from layout import assembly, layer, parts as layout_parts, projects as project_paths, implementation_group, owned_sources
from module_rules import owner


def classify(path, symbol):
    parts = layout_parts(path)
    project = parts[1]
    if path.startswith("modules/"):
        return Path(path).parts[1], {"src": "production", "tests": "conformité", "testing": "fixture"}[parts[0]]
    if parts[0] == "tests":
        return "Tests", "conformité"
    if parts[0] == "benchmarks":
        return "Évaluation", "mesure"
    if "obj" in parts:
        return project, "généré"
    if "Migrations" in parts:
        return project, "historique EF"
    if project in {"Alycia.DevHost", "Alycia.Persistence.Migrations"}:
        return "Maître", "composition de développement" if project == "Alycia.DevHost" else "intégration EF"
    if project == "Alycia.Api":
        return "API", "composition" if "Registration" in path or path.endswith("/Program.cs") else "transport"
    if project == "Alycia.Contracts":
        return "Contracts", "contrat"
    if project == "Alycia.Client.Avalonia":
        return "Boréale", "présentation"
    if ".Workspaces." in project:
        return "Workspaces", project.rsplit(".", 1)[1]
    if project.endswith("Chat.Domain"):
        target = {"WorkspaceProfile": "Workspaces", "WorkspaceFileOverride": "Documents",
                  "WorkspaceNote": "Workspaces", "Attachment": "Documents", "DocumentAnalysis": "Documents",
                  "PromptProfile": "Configuration"}.get(Path(path).stem, "Conversations")
        if "MemoryLedgerJson" in symbol or ".SetMemory" in symbol:
            target = "Memory"
        return target, "état persistant"
    area = owner(path, symbol)
    if not area:
        raise ValueError(f"Propriétaire absent pour {path} ({symbol})")
    return ("Workspaces" if area == "Projects" else area,
            "adaptateur" if project.endswith(".Infrastructure") else "application")


def collect(input_dir, output_dir):
    declarations = []
    accesses = []
    for file in sorted(input_dir.glob("*.tsv")):
        with file.open() as stream:
            for row in csv.reader(stream, delimiter="\t", quoting=csv.QUOTE_NONE):
                kind, path = row[:2]
                if layer(path) not in {"src", "tests", "testing", "benchmarks"} or "/obj/" in path:
                    continue
                if kind == "D":
                    _, _, line, category, access, identifier = row
                    full_name = identifier[2:].partition("(")[0]
                    name = full_name.rsplit(".", 1)[-1] if identifier.startswith("T:") else full_name.rsplit(".", 2)[-2]
                    target, role = classify(path, name)
                    if ".WorkspaceFileOverride" in identifier:
                        target = "Documents"
                    declarations.append((path, int(line), category, access, identifier, file.stem, target, role))
                elif kind in ("E", "S", "Y", "L", "C") and layer(path) == "src" and "/obj/" not in path:
                    accesses.append((path, int(row[2]), kind, " | ".join(row[3:])))
    output_dir.mkdir(parents=True, exist_ok=True)
    for name, columns, data in [
        ("D01-ownership.tsv", ("fichier", "ligne", "genre", "visibilité", "identifiant Roslyn", "assembly actuelle", "propriétaire cible", "rôle"), declarations),
        ("D01-access.tsv", ("fichier", "ligne", "type", "cible"), accesses),
    ]:
        with (output_dir / name).open("w", newline="", encoding="utf-8") as stream:
            writer = csv.writer(stream, delimiter="\t", lineterminator="\n")
            writer.writerow(columns)
            writer.writerows(sorted(data))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input_dir", type=Path)
    parser.add_argument("output_dir", type=Path)
    args = parser.parse_args()
    collect(args.input_dir, args.output_dir)

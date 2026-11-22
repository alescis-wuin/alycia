#!/usr/bin/env python3
"""Reconcile the D20 removable rules, logical edges and ports against the compiler output."""
import argparse
import csv
from collections import defaultdict
import importlib.util
from pathlib import Path
import re
import sys

from layout import assembly, layer, parts as layout_parts, projects as project_paths, implementation_group, owned_sources
from module_rules import owner

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
spec = importlib.util.spec_from_file_location("guard", ROOT / "scripts/check-modules.py")
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)

# The test names refer to suites in this repository.
ACTIONS = {
    "Configuration": ("D21", "Projection immuable par consommateur", "ConversationSettingsTransferTests"),
    "Contracts": ("D21", "Contrat public sans fournisseur", "ConversationSettingsTransferTests"),
    "Projects": ("D22", "Lecture ou commande propriétaire du profil", "ProjectCreationTests"),
    "Workspaces": ("D22", "Identité ou profil du projet dans la session partagée", "ProjectCreationTests"),
    "Conversations": ("D27", "Commande du tour ou lecture de messages bornée", "StreamingTests"),
    "Documents": ("D23", "Source autorisée par propriétaire, ID et empreinte", "ProjectFilesTests"),
    "Indexing": ("D24", "Découverte paginée et publication versionnée", "EmbeddingIndexBoundaryTests"),
    "Memory": ("D25", "Projection de rappel et préparation du ledger", "TemporalMemoryIntegrationTests"),
    "Context": ("D25", "Instantané et résultat du contexte", "ConversationContextPreparationTests"),
    "Tools": ("D26", "Capacité de lecture bornée au snapshot", "ProjectToolStorageTests"),
    "Retrieval": ("D26", "Requête de classement sans entité persistée", "ProjectRetrievalTests"),
    "Inference": ("D21", "Requête de génération et identité du fournisseur", "TextGenerationContractTests"),
    "Persistence": ("D22", "Session technique enrôlée", "ProjectCreationTests"),
}


def dump(path, header, rows):
    with path.open("w", newline="") as stream:
        writer = csv.writer(stream, delimiter="\t", lineterminator="\n")
        writer.writerow(header)
        writer.writerows(rows)


def sites(places):
    grouped = defaultdict(set)
    for path, line in places:
        grouped[path].add(int(line))
    return " | ".join(path + ":" + ",".join(map(str, sorted(lines))) for path, lines in sorted(grouped.items()))


def export(capture, destination):
    destination.mkdir(parents=True, exist_ok=True)
    rows = [r.split("\t") for f in sorted(capture.glob("*.tsv")) for r in f.read_text().splitlines()]
    errors, matrix = guard.check_records(rows, ROOT)
    if errors:
        raise ValueError("\n".join(errors))
    types = {(assembly(r[1]), r[5]): r[1] for r in rows if r[0] == "D" and r[3] == "NamedType"
             and layer(r[1]) == "src" and "/obj/" not in r[1]}
    exceptions, edges, evidence = defaultdict(set), defaultdict(set), defaultdict(set)
    for r in rows:
        kind, path = r[:2]
        if not layer(path) == "src" or "/obj/" in path or "/Migrations/" in path:
            continue
        if kind in {"E", "S", "Y"}:
            resource = guard.SETS.get(r[4], r[4]) if kind == "E" else r[3]
            exceptions[path, kind, resource].add((path, r[2]))
        elif kind == "R":
            _, _, line, caller, assembly, identifier, target = r
            declaration = types.get((assembly, identifier))
            if not declaration:
                continue
            left, right = owner(path, caller), owner(declaration, target)
            if left and right and left != right:
                edges[left, right].add((path, line))
                exceptions[path, "R", right + "." + target].add((path, line))
        elif kind == "L":
            evidence["DI", path, r[3] + " " + r[4]].add((path, r[2]))
    if set(edges) != set(matrix):
        raise ValueError("Graphe logique divergent")
    # Include explicit allocations/factories and file operations absent from the D19 analyzer.
    pattern = re.compile(r"\b(?:new\s+(?:Chat|Workspaces)DbContext\b|CreateDbContext(?:Async)?\b|(?:File|Directory|FileStream|FileStorage)\s*[.(]|\.PathFor\s*\()")
    for file in sorted(owned_sources(ROOT, "src")):
        if {"obj", "bin", "Migrations"} & set(file.parts):
            continue
        path = file.relative_to(ROOT).as_posix()
        for n, line in enumerate(file.read_text(encoding="utf-8-sig").splitlines(), 1):
            for match in pattern.finditer(line):
                evidence["appel source", path, match[0].strip()].add((path, n))
    transitions = []
    for path, kind, resource, *_ in list(csv.reader((ROOT / "scripts/architecture/transition.tsv").open(), delimiter="\t"))[1:]:
        area = owner(path) or ("Indexing" if "Modules.Indexing/" in path else "Transport")
        target = guard.TABLE_OWNERS.get(resource, resource.partition(".")[0])
        phase, action, test = ACTIONS.get(area, ("D29", "Borner la résolution dynamique", "architecture_guard_tests"))
        if kind == "Y":
            phase, action, test = "D29", "Examiner le symbole et borner son usage", "architecture_guard_tests"
        lines = exceptions[path, kind, resource]
        if not lines:
            action = "Supprimer la dérogation sans usage compilé et rejouer la garde"
        transitions.append([kind, resource, area, target, phase, action, test, "élevée" if kind != "Y" else "à auditer", sites(lines) or "aucun"])
    dump(destination / "D20-derogations.tsv", ["type", "ressource", "appelant", "propriétaire cible", "phase", "action", "test", "criticité", "sites"], transitions)
    dump(destination / "D20-relations.tsv", ["appelant", "cible", "types", "phase", "action", "test", "sites"], [
        (left, right, ", ".join(sorted(matrix[left, right])), *ACTIONS[left], sites(places)) for (left, right), places in sorted(edges.items())])
    dump(destination / "D20-appels.tsv", ["type", "fichier", "opération", "sites"], [
        (*key, sites(places).split(":", 1)[-1]) for key, places in sorted(evidence.items())])

    with (ROOT / "docs/architecture/decouplage/D19-matrice-contrats.csv").open() as stream:
        ports = list(csv.reader(stream))[1:]
    port_rows = []
    for area, name, path, impl, double, local, suite in ports:
        short = name.rsplit(".", 1)[-1]
        where = [(path, i) for i, text in enumerate((ROOT / path).read_text().splitlines(), 1)
                 if re.search(r"\binterface\s+" + short + r"\b", text)]
        if not where:
            raise ValueError("Déclaration introuvable : " + name)
        phase, action, test = ACTIONS[area]
        if short in {"IGenerationSettings", "IIndexSettings", "IToolWindowSettings"}:
            role, reason = "projection pure", "Propriétés en lecture seule ; tester la capture et les valeurs"
        elif short in {"IChatTurnScope", "IForegroundActivity", "ITextTokenCounter"}:
            role, reason = "technique", "Durée de vie transactionnelle, ordonnanceur ou compteur du fournisseur"
        else:
            role, reason = "comportemental", "Résultats ou effets observables par le consommateur"
        port_rows.append((area, name, sites(where), role, reason, phase, action, test, double, suite))
    dump(destination / "D20-ports.tsv", ["propriétaire", "port", "déclaration", "classe", "raison", "phase",
         "action", "test ciblé", "double", "conformité commune existante"], port_rows)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    export(args.capture, args.destination)

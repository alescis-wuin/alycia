#!/usr/bin/env python3
"""Rebuild the final port and type inventories from compiler output and executed TRX."""
import argparse
import csv
import importlib.util
from collections import defaultdict
from pathlib import Path
import xml.etree.ElementTree as ET

from module_rules import module_owner

ROOT = Path(__file__).resolve().parents[2]
LOCATION = ROOT / "docs/architecture/decouplage/finition"
SPEC = importlib.util.spec_from_file_location("export_review", Path(__file__).with_name("export-review.py"))
exporter = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(exporter)

# Each method runs the same assertions against both providers. Names never include test arguments.
PAIRS = {
    "ITextGeneration": "Text_and_stream_agree_and_enforce_capabilities",
    "ILocalEmbeddings": "Vector_provider_preserves_row_order_identity_and_cancellation",
    "IImportLimitsReader": "Import_limits_preserve_defaults_overrides_and_cancellation_in_both_adapters",
    "IConversationIndexOwners": "Conversation_pages_and_tool_policies_agree_on_scope_cursors_and_cancellation",
    "IToolConversationPolicy": "Conversation_pages_and_tool_policies_agree_on_scope_cursors_and_cancellation",
    "IConversationWorkspaceLookup": "Conversation_pages_and_tool_policies_agree_on_scope_cursors_and_cancellation",
    "IProjectIndexOwners": "Workspace_identity_and_active_pages_share_the_same_behavior_with_their_doubles",
    "IProjectModelDefaults": "Workspace_identity_and_active_pages_share_the_same_behavior_with_their_doubles",
    "IWorkspaceIdentityDirectory": "Workspace_identity_and_active_pages_share_the_same_behavior_with_their_doubles",
    "IAuthorizedToolSources": "Document_tools_live_permissions_and_index_use_the_same_scope_on_both_adapters",
    "ILiveIndexFileAccess": "Document_tools_live_permissions_and_index_use_the_same_scope_on_both_adapters",
    "IProjectIndex": "Document_tools_live_permissions_and_index_use_the_same_scope_on_both_adapters",
    "IPassageReranker": "Verified_weights_and_the_recorded_adapter_preserve_positions_identity_and_cancellation",
    "IMemoryVectorEligibility": "Eligibility_obeys_owner_sequence_date_and_cancellation_in_both_adapters",
}


def write(path, header, rows):
    with path.open("w", newline="") as out:
        writer = csv.writer(out, delimiter="\t", lineterminator="\n")
        writer.writerow(header)
        writer.writerows(rows)


def successful_methods(directory):
    cases = defaultdict(list)
    for trx in directory.glob("*.trx"):
        root = ET.parse(trx)
        definitions = {test.get("id"): test.find("{*}TestMethod") for test in root.findall(".//{*}UnitTest")}
        for case in root.findall(".//{*}UnitTestResult"):
            method = definitions.get(case.get("testId"))
            if method is not None:
                cases[method.get("name", "")].append(case.get("outcome"))
    return cases


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("capture", type=Path)
    parser.add_argument("trx", type=Path)
    parser.add_argument("out", type=Path)
    parser.add_argument("--schema", type=Path, help="CSV du catalogue de contraintes PostgreSQL")
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    rows = [line.split("\t") for f in sorted(args.capture.glob("*.tsv")) for line in f.read_text().splitlines()]
    declared = {(r[1].split("/")[1], r[5]): r[1] for r in rows if r[0] == "D" and r[3] == "NamedType"
                and r[1].startswith("src/") and "/obj/" not in r[1]}
    ports = {r[2]: r[1] for r in rows if r[0] == "I" and r[1].startswith("src/") and "/obj/" not in r[1]}
    implementations = defaultdict(lambda: defaultdict(set))
    for r in rows:
        if r[0] == "M" and r[3] in ports and "/obj/" not in r[1]:
            path = Path(r[1]).parts
            group = "double" if path[1] == "Alycia.Modules.Testing" else path[0]
            implementations[r[3]][group].add(r[2])
    with (LOCATION / "D20-ports.tsv").open() as source:
        baseline = {r["port"].rsplit(".", 1)[-1]: r for r in csv.DictReader(source, delimiter="\t")}
    methods = successful_methods(args.trx)
    contracts = []
    for port, path in sorted(ports.items()):
        name = port.rsplit(".", 1)[-1]
        initial = baseline.get(name)
        category = initial["classe"] if initial else "comportemental"
        proof = PAIRS.get(name, "")
        expected = 2 if name in {"ITextGeneration", "ILocalEmbeddings"} else 1
        ran = bool(proof) and methods.get(proof) == ["Passed"] * expected
        groups = implementations[port]
        if proof and (not ran or not groups["double"] or not groups["src"]):
            raise ValueError("Conformité non vérifiée: " + port + " (" + proof + ")")
        status = "VALIDÉ" if ran else ("EXCLU" if category != "comportemental" else "OUVERT")
        detail = initial["raison"] if status == "EXCLU" else (proof or "Suite commune et double à démontrer")
        module = initial["propriétaire"] if initial else (exporter.OWNERS.get(name) or module_owner(path, name) or Path(path).parent.name)
        contracts.append(["Workspaces" if module == "Projects" else module,
                          port, "D20" if initial else "ajouté", category, status, path, " | ".join(sorted(groups["src"])),
                          " | ".join(sorted(groups["double"])), detail])
    write(args.out / "D29-ports.tsv", ["propriétaire","port","origine","classe","statut","déclaration","adaptateurs","doubles","preuve ou justification"], contracts)

    if args.schema:
        with args.schema.open() as f:
            actual = list(csv.DictReader(f, delimiter="\t" if args.schema.suffix == ".tsv" else ","))
        with (LOCATION / "D20-schema.tsv").open() as f:
            reference = list(csv.DictReader(f, delimiter="\t"))
        fields = ("schema", "relation", "constraint_name", "definition")
        def ordered(records):
            return sorted(tuple(r[k] for k in fields) for r in records)
        if ordered(actual) != ordered(reference):
            raise ValueError("Le catalogue PostgreSQL diffère de D20")
        tables = defaultdict(list)
        for r in actual:
            tables[r["schema"] + "." + r["relation"]].append(r["constraint_name"])
        accesses = defaultdict(lambda: defaultdict(set))
        for r in rows:
            if r[0] in {"E", "S"} and r[1].startswith("src/") and "/Migrations/" not in r[1] and "/obj/" not in r[1]:
                table = exporter.guard.SETS.get(r[4], r[4]) if r[0] == "E" else r[3]
                accesses[table][r[0]].add(r[1] + ":" + r[2])
        owners = exporter.guard.TABLE_OWNERS
        if set(accesses) - set(owners) or set(owners) - set(tables):
            raise ValueError("Table sans propriétaire ou absente du catalogue")
        table_rows = []
        for table, constraints in sorted(tables.items()):
            schema, name = table.split(".")
            who = ("EF migrations " + schema) if name == "__EFMigrationsHistory" else owners[table]
            ef, sql = (sorted(accesses[table][kind]) for kind in ("E", "S"))
            table_rows.append([table, who, " | ".join(sorted(constraints)), len(ef), len(sql),
                               " | ".join(ef) or "—", " | ".join(sql) or "—"])
        write(args.out / "D29-propriete.tsv", ["table", "propriétaire", "contraintes PostgreSQL", "sites EF",
              "sites SQL", "sources EF", "sources SQL"], table_rows)

    edges = defaultdict(set)
    for r in rows:
        if r[0] != "R" or "/obj/" in r[1] or "/Migrations/" in r[1]: continue
        source = module_owner(r[1], r[3])
        target_path = declared.get((r[4], r[5]))
        target = module_owner(target_path, r[6]) if target_path else None
        if source and target and source != target:
            edges[(source, target, r[5][2:], "interface" if r[5][2:] in ports else "type hors Contracts")].add(r[1] + ":" + r[2])
    write(args.out / "D29-usages.tsv", ["source","destination","type","genre","sites Roslyn"], [
        [*key," | ".join(sorted(sites))] for key, sites in sorted(edges.items())])
    graph = [[r[2], Path(ref).stem] for r in rows if r[0] == "A" for ref in
             sorted((args.capture / (r[2] + ".refs")).read_text(encoding="utf-8-sig").splitlines())]
    write(args.out / "D29-projets.tsv", ["projet","référence MSBuild"], sorted(graph))


if __name__ == "__main__":
    main()

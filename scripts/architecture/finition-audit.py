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
    **dict.fromkeys(("IAnalysisConversation","IArchiveDocuments","IDocumentFork"), "Analysis_reads_committed_revisions_and_forks_detach_only_their_own_originals"),
    **dict.fromkeys(("IQueryPlanner","ITurnRouter","IRetrievalPipeline","IProjectRetrieval"), "Planning_and_retrieval_respect_the_same_owners_pins_and_semantic_callback_scope"),
    **dict.fromkeys(("ITemporalMemory","ITemporalMemoryIndex","IIndexMaintenance"), "WorkerRebuildsFromOriginalsAndScopesCorrectionsAndHistoricalViews"),
    "IHistoryKnowledge": "ResumesSmallSlicesPublishesWholeTreesAndRespectsScope",
    "IIndexedEmbeddings": "Similarity_adapters_check_owner_model_dimension_and_historical_cutoffs",
    **dict.fromkeys(("IChatRepository","IConversationMemory"), "Conversation_ports_rollback_the_pair_and_commit_memory_at_the_expected_revision"),
    "IContextMemory": "Context_memory_reports_only_verbatim_quotes_and_provenance_with_bounded_recall",
    **dict.fromkeys(("ITurnContext","ITurnTools","ITurnResponseAudit"), "Context_tools_and_audit_preserve_the_prompt_stream_refusals_and_cancellation"),
    **dict.fromkeys(("IConversationContextReader", "IToolHistoryReader", "IQuerySources", "ITurnHistory", "ITurnDocuments", "ITemporalHistorySources"),
        "Scoped_history_and_documents_share_paging_visibility_immutability_and_cancellation"),
    **dict.fromkeys(("IProjectCatalog", "IProjectNoteOrigins", "IWorkspaceProfiles"),
        "Catalog_profiles_and_note_origins_enforce_revisions_archives_and_provenance"),
    "IChatModel": "Chat_model_adapters_preserve_stream_content_stop_and_supported_capabilities",
    **dict.fromkeys(("IModelMeter", "IModelMeterFactory"), "Meter_factories_isolate_operations_and_snapshot_partial_provider_usage"),
    "IEmbeddingSimilarityReader": "Similarity_adapters_check_owner_model_dimension_and_historical_cutoffs",
    **dict.fromkeys(("IConversationOriginals", "IConversationFileChanges", "IDocumentMembershipOverrides"),
        "Original_readers_and_revision_staging_distinguish_missing_private_unavailable_and_foreign"),
    "ISlidingHistory": "History_selection_keeps_complete_owner_pairs_within_both_budgets",
    "IPassageScoring": "Lexical_adapters_preserve_positions_dimension_and_rank_relevant_files_first",
    **dict.fromkeys(("IProjectTools", "ISnapshotProjectTools", "ISourceCommands"),
        "Tools_scope_pages_and_refusals_survive_contract_and_snapshot_dispatch"),
    "ISummaryReducer": "Summary_providers_respect_empty_content_byte_budgets_call_limits_and_cancellation",
    "IQueryBriefRefiner": "Refiners_admit_only_verbatim_intents_and_do_not_alias_caller_arrays",
    "IWorkspaceRepository": "Workspace_repositories_agree_on_identity_case_uniqueness_and_cancelled_writes",
    "ITemporalFactStorage": "Fact_storage_preserves_first_observation_orders_exports_and_bounds_search_by_owner_and_date",
    "IConversationLease": "Reservations_are_owner_scoped_and_old_disposal_cannot_release_the_new_holder",
    "ITextGeneration": "Text_and_stream_agree_and_enforce_capabilities",
    "ILocalEmbeddings": "Vector_provider_preserves_row_order_identity_and_cancellation",
    "IIndexSourceDocuments": "Document_index_pages_preserve_cursor_scope_digest_and_exclusion",
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
    parser.add_argument("--require-complete", action="store_true", help="fail on any behavioral port without common passing checks")
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
            group = "double" if path[1] == "Alycia.Modules.Testing" or path[0] == "tests" and "Doubles" in path else path[0]
            implementations[r[3]][group].add(r[2])
    with (LOCATION / "D20-ports.tsv").open() as source:
        baseline = {r["port"].rsplit(".", 1)[-1]: r for r in csv.DictReader(source, delimiter="\t")}
    methods = successful_methods(args.trx)
    excluded_checks = {
        "IGenerationSettings": ("Projection en lecture seule des paramètres du générateur.", "RoundTripKeepsEveryPersistedOptionWithoutRoundingOrRuntimeData"),
        "IIndexSettings": ("Projection en lecture seule du profil et des préfixes d’index.", "Caller_cannot_expand_a_pending_index_request"),
        "IToolWindowSettings": ("Projection de valeurs utilisée pour la réservation du budget d’outils.", "DefaultWindowReservesAllNineToolsAndStillFitsTheProtectedQuestion"),
        "IForegroundActivity": ("Réservation locale de priorité et libération au terme du scope.", "ResumesSmallSlicesPublishesWholeTreesAndRespectsScope"),
        "ITextTokenCounter": ("Primitive de comptage dépendante du vocabulaire du fournisseur.", "MatchesIndependentHuggingFaceCountsForFrenchCodeAndCjk"),
        "IChatTurnScope": ("Durée de vie et terminaison de la transaction explicitement exercées sur les deux dépôts.", "Conversation_ports_rollback_the_pair_and_commit_memory_at_the_expected_revision"),
    }
    tests = {p.relative_to(ROOT).as_posix(): p.read_text(encoding="utf-8-sig") for p in sorted((ROOT/"tests").glob("**/*.cs")) if "obj" not in p.parts}
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
        method = proof
        if status == "EXCLU":
            detail, method = excluded_checks[name]
            if not methods.get(method) or any(result != "Passed" for result in methods[method]):
                raise ValueError("Justification sans test réussi : " + port)
        files = [file for file, code in tests.items() if method and (" " + method + "(") in code]
        if method and len(files) != 1:
            raise ValueError("Déclaration du test absente ou ambiguë : " + method)
        fixture = files[0] if files else ""
        command = 'dotnet test ' + str(Path(fixture).parent) + ' --filter FullyQualifiedName~' + method if fixture else ""
        if method == "Verified_weights_and_the_recorded_adapter_preserve_positions_identity_and_cancellation":
            environment = "ONNX Runtime CPU, poids et tokenizer vérifiés"
        elif fixture and ("[PostgresFact]" in tests[fixture] or "[DatabaseFact]" in tests[fixture]):
            environment = "PostgreSQL natif jetable / fournisseur de test selon la méthode"
        elif "Inference.Tests/" in fixture:
            environment = "HTTP Ollama contrôlé et fournisseur déterministe"
        else:
            environment = ".NET Linux / adaptateur local et fixtures contrôlées"
        contracts.append(["Workspaces" if module == "Projects" else module,
                          port, "D20" if initial else "ajouté", category, status, path, " | ".join(sorted(groups["src"])),
                          " | ".join(sorted(groups["double"])), detail, fixture, command, environment, "Passed" if method else "NON_TESTÉ"])
    write(args.out / "D29-ports.tsv", ["propriétaire","port","origine","classe","statut","déclaration","adaptateurs","doubles","preuve ou justification","fixture","commande","environnement","résultat TRX"], contracts)

    if args.require_complete and any(r[4] == "OUVERT" for r in contracts):
        raise SystemExit("Ports ouverts : " + ", ".join(r[1] for r in contracts if r[4] == "OUVERT"))

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
        if r[0] != "R" or not r[1].startswith("src/") or "/obj/" in r[1] or "/Migrations/" in r[1]: continue
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

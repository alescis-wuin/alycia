#!/usr/bin/env python3
"""Compare a fresh acceptance run to the frozen D00 controls."""
import argparse
from collections import Counter
import csv
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / "docs/architecture/decouplage"
MEASUREMENTS = {"p50Ms", "p95Ms", "milliseconds", "allocatedBytes"}


def canonical(value):
    if isinstance(value, list):
        return [canonical(item) for item in value]
    if isinstance(value, dict):
        return {key: canonical(item) for key, item in value.items() if key not in MEASUREMENTS}
    return value


def digest(data):
    return hashlib.sha256(data).hexdigest()


def compare(args):
    baseline = json.loads((DOCS / "baseline.json").read_text())
    http = json.loads(args.http.read_text())
    rag = json.loads(args.rag.read_text())
    api = args.openapi.read_bytes()
    fixture = (ROOT / "modules/conversations/tests/Alycia.Modules.Conversations.Tests/Fixtures/conversation-settings-v1.json").read_bytes()
    # The tracked text fixture adds exactly one LF. The functional xUnit case compares
    # the preceding bytes directly to the production serializer's output.
    exported_fixture = fixture.removesuffix(b"\n")
    serializer_case = ("alycia.modules.chat.tests.dll",
                       "Alycia.Modules.Chat.Tests.ConversationSettingsTransferTests.ReadsAndReproducesTheFileExportedByTheFunctionalBaseline")
    from test_identity import move_map
    moves = move_map()
    runs = Counter()
    outcomes = Counter()
    counts = {}
    for file in sorted(args.trx.glob("*.trx")):
        tree = ET.parse(file)
        assembly = Path(tree.find(".//{*}UnitTest").get("storage")).name.lower()
        if assembly in counts:
            raise ValueError(f"Multiple TRX for {assembly}: use an empty results directory for each run")
        counts[assembly] = {k: int(v) for k, v in tree.find(".//{*}Counters").attrib.items()
                            if k in {"total", "passed", "failed", "notExecuted"}}
        definitions = {t.get("id"): t.find("{*}TestMethod") for t in tree.findall(".//{*}UnitTest")}
        for result in tree.findall(".//{*}UnitTestResult"):
            method = definitions[result.get("testId")]
            old, new = moves(method.get("className"), method.get("name"), assembly)
            if assembly != new:
                raise ValueError("Unexpected destination assembly: " + assembly)
            key = (old, result.get("testName"))
            runs[key] += 1
            outcomes[result.get("outcome")] += 1
    with (DOCS / "D00-tests.tsv").open() as stream:
        expected = Counter((row["suite"], row["test"]) for row in csv.DictReader(stream, delimiter="\t"))
    missing = expected - runs
    rag_hash = digest(json.dumps(canonical(rag), sort_keys=True, separators=(",", ":")).encode())
    evidence = {
        "tests": counts, "baselineTestsPresent": expected.total() - missing.total(), "newTestCases": (runs - expected).total(),
        "httpScenarios": len(http["checks"]), "openApiPaths": len(json.loads(api)["paths"]), "openApiSha256": digest(api),
        "ragCanonicalSha256": rag_hash, "ragOrderedRows": sum(len(run["rows"]) for group in ("runs", "stress") for run in rag[group].values()),
        "ragClaims": len(rag["claims"]["rows"]), "ragClassifications": len(rag["classification"]["rows"]),
        "settingsV1": {"bytes": len(exported_fixture), "sha256": digest(exported_fixture)}
    }
    controls = {
        "baselineTests": not missing and baseline["tests"].keys() <= counts.keys(),
        "allDotnetCasesPassed": runs.total() == sum(c["total"] for c in counts.values())
            and counts and outcomes["Passed"] == runs.total(),
        "orderedHttpScenarios": http["checks"] == baseline["httpChecks"] and http["passed"] == baseline["httpScenarios"],
        "openApiBytes": digest(api) == baseline["openApiSha256"],
        "orderedRag": rag_hash == baseline["ragCanonicalSha256"] and rag["failedThresholds"] == [],
        "configurationV1Bytes": fixture.endswith(b"\n") and runs[serializer_case] == 1
            and evidence["settingsV1"] == baseline["settingsV1"]
    }
    print(json.dumps({**evidence, "controls": controls}, indent=2))
    if not all(controls.values()):
        raise SystemExit("Baseline mismatch: " + ", ".join(key for key, value in controls.items() if not value))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("http", "rag", "openapi", "trx"):
        parser.add_argument("--" + name, type=Path, required=True)
    compare(parser.parse_args())

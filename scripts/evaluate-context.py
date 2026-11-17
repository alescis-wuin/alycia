#!/usr/bin/env python3
"""Évaluation locale reproductible ; les indicateurs lexicaux nécessitent une revue humaine."""
import argparse
import hashlib
import importlib.util
import io
import json
import math
import re
import time
import uuid
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "evaluation/context-v1"


def load_suite(directory=DATA):
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    files = {}
    for entry in manifest["files"]:
        path = Path(entry["path"])
        if path.is_absolute() or ".." in path.parts:
            raise ValueError("Chemin de corpus invalide")
        data = (directory / "corpus" / path).read_bytes()
        if hashlib.sha256(data).hexdigest() != entry["sha256"] or len(data) != entry["bytes"]:
            raise ValueError("Corpus modifié : " + str(path))
        files[entry["path"]] = data
    cases = json.loads((directory / "cases.json").read_text(encoding="utf-8"))
    if len({c["id"] for c in cases}) != len(cases):
        raise ValueError("Identifiants de questions dupliqués")
    for case in cases:
        if case["split"] not in ("development", "holdout") or not set(case["expectedSources"]) <= files.keys():
            raise ValueError("Référence de question invalide")
        for pattern in case["factPatterns"] + case["forbiddenPatterns"]:
            re.compile(pattern, re.I | re.S)
    fingerprint = hashlib.sha256((directory / "manifest.json").read_bytes() + (directory / "cases.json").read_bytes()).hexdigest()
    return files, cases, fingerprint


def indicators(case, answer):
    # No automatic 'correctness' score: paraphrases and negations need human review.
    return {"factPatternHits": [bool(re.search(p, answer, re.I | re.S)) for p in case["factPatterns"]],
            "forbiddenPatternHits": [bool(re.search(p, answer, re.I | re.S)) for p in case["forbiddenPatterns"]],
            "expectedPathsMentioned": [p in answer or Path(p).name in answer for p in case["expectedSources"]],
            "humanReview": {"correctFacts": None, "missingFacts": None, "unsupportedClaims": None, "notes": ""}}


def retrieval_metrics(case, sources, k=5):
    """File-level relevance, not proof of factual correctness or passage recall."""
    expected = set(case["expectedSources"])
    ranked = list(dict.fromkeys(s["path"].split("!/")[-1] for s in sources))[:k]
    hits = [path in expected for path in ranked]
    found = sum(hits)
    dcg = sum(int(hit) / math.log2(i + 2) for i, hit in enumerate(hits))
    ideal = sum(1 / math.log2(i + 2) for i in range(min(k, len(expected))))
    return {"status": "measured", "unit": "file", "k": k, "retrieved": len(ranked),
            "expected": len(expected), "relevantRetrieved": found,
            "recall": found / len(expected) if expected else None,
            "precision": found / len(ranked) if ranked else None,
            "ndcg": dcg / ideal if ideal else None,
            "noAnswerCorrectlyEmpty": not ranked if not expected else None}


def compare(left, right):
    keys = ("suiteSha256", "model", "contextTokens", "maxOutputTokens", "toolMaxCalls", "tools", "seed", "repeats", "split")
    mismatch = [k for k in keys if left.get(k) != right.get(k)]
    if mismatch:
        raise ValueError("Runs non comparables : " + ", ".join(mismatch))
    if left.get("modelIdentity") and right.get("modelIdentity") and left["modelIdentity"] != right["modelIdentity"]:
        raise ValueError("Identité de modèle différente")
    a = {(r["id"], r["repeat"]): r for r in left["results"]}
    b = {(r["id"], r["repeat"]): r for r in right["results"]}
    if a.keys() != b.keys():
        raise ValueError("Questions manquantes dans un run")
    if len(a) != len(left["results"]) or len(b) != len(right["results"]):
        raise ValueError("Résultats dupliqués")
    if not a or any(r.get("error") for r in [*a.values(), *b.values()]):
        raise ValueError("Run vide ou incomplet")
    return {"labels": [left["label"], right["label"]], "sameProtocol": True,
            "modelIdentityRequiresReview": left.get("modelIdentity") is None or right.get("modelIdentity") is None,
            "rows": [{"id": key[0], "repeat": key[1], "elapsedSeconds": [a[key]["elapsedSeconds"], b[key]["elapsedSeconds"]],
                      "indicators": [a[key].get("indicators"), b[key].get("indicators")],
                      "usage": [a[key].get("usage"), b[key].get("usage")],
                      "previewUsage": [a[key].get("previewUsage"), b[key].get("previewUsage")],
                      "options": [a[key].get("options"), b[key].get("options")],
                      "retrieval": [a[key].get("retrieval"), b[key].get("retrieval")],
                      "answers": [a[key].get("answer"), b[key].get("answer")],
                      "errors": [a[key].get("error"), b[key].get("error")]} for key in sorted(a)],
            "notice": "Examiner les réponses et les preuves ; aucune hausse de qualité n’est inférée des seuls motifs lexicaux."}


def run(args):
    files, cases, fingerprint = load_suite(args.suite)
    selected = [c for c in cases if args.split == "all" or c["split"] == args.split]
    spec = importlib.util.spec_from_file_location("verify_local", ROOT / "scripts/verify-local.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    call = module.create_client(args.url)
    args.output.mkdir(parents=True, exist_ok=False)
    archive = io.BytesIO()
    with zipfile.ZipFile(archive, "w") as z:
        for path, data in files.items():
            z.writestr(path, data)
    report = {"schemaVersion": 1, "label": args.label, "model": args.model, "modelIdentity": None,
              "suiteSha256": fingerprint, "contextTokens": args.context, "maxOutputTokens": args.output_tokens,
              "toolMaxCalls": args.calls, "tools": args.tools, "seed": args.seed, "repeats": args.repeats,
              "split": args.split, "results": [], "cleaned": False}
    report_path = args.output / "run.json"

    def save():
        report_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    remaining = []
    try:
        for case in selected:
            for repeat in range(args.repeats):
                cid = None
                row = {"id": case["id"], "repeat": repeat, "question": case["question"]}
                started = time.monotonic()
                try:
                    conversation = call("POST", "/conversations", {"title": "Évaluation " + case["id"], "model": args.model,
                        "systemPrompt": "Réponds en français à partir des sources importées. Cite les fichiers utilisés et signale les informations absentes."}, expected=201)
                    cid = conversation["id"]
                    remaining.append(cid)
                    prefix = "/conversations/" + cid
                    settings = call("GET", prefix + "/settings")
                    options = settings["options"]
                    options.update(contextTokens=args.context, maxOutputTokens=args.output_tokens, temperature=0,
                                   seed=args.seed, generateUntilStop=False, useProjectTools=args.tools, toolMaxCalls=args.calls,
                                   safetyTokens=256, summaryMaxCalls=256)
                    requested = {}
                    if args.retrieval: requested["retrievalMode"] = args.retrieval
                    if args.embedding_model: requested["embeddingModel"] = args.embedding_model
                    if args.counter: requested["tokenCounting"] = args.counter
                    if args.memory: requested["structuredMemory"] = args.memory == "structured"
                    options.update(requested)
                    call("PUT", prefix + "/settings", {"revision": conversation["revision"], "model": args.model,
                        "systemPrompt": conversation["systemPrompt"], "contextNotes": "", "options": options})
                    applied = call("GET", prefix + "/settings")["options"]
                    if any(applied.get(key) != value for key, value in requested.items()):
                        raise RuntimeError("Option demandée non prise en charge par cette version")
                    row["options"] = applied
                    boundary = "evaluation-" + uuid.uuid4().hex
                    body = (f'--{boundary}\r\nContent-Disposition: form-data; name="files"; filename="corpus.zip"\r\n'
                            'Content-Type: application/zip\r\n\r\n').encode() + archive.getvalue() + f'\r\n--{boundary}--\r\n'.encode()
                    call("POST", prefix + "/files/" + str(uuid.uuid4()), raw=body,
                         headers={"Content-Type": "multipart/form-data; boundary=" + boundary})
                    row["historyTurns"] = []
                    for turn in case.get("turns", []):
                        revision = call("GET", prefix + "/settings")["conversation"]["revision"]
                        events = call("POST", prefix + "/stream", {"requestId": str(uuid.uuid4()), "revision": revision, "content": turn})
                        if not events or events[-1]["type"] != "done":
                            raise RuntimeError("Historique interrompu avant la question de rappel")
                        row["historyTurns"].append(events[-1]["result"].get("usage"))
                    preview = call("POST", prefix + "/context", {"content": case["question"]})
                    row["preview"] = preview["context"]
                    row["retrieval"] = retrieval_metrics(case, row["preview"].get("sources") or [])
                    row["previewUsage"] = preview.get("usage")
                    revision = call("GET", prefix + "/settings")["conversation"]["revision"]
                    events = call("POST", prefix + "/stream", {"requestId": str(uuid.uuid4()), "revision": revision, "content": case["question"]})
                    if not events or events[-1]["type"] != "done":
                        raise RuntimeError("Réponse non terminée : " + str(events[-1].get("message", "") if events else "flux vide"))
                    result = events[-1]["result"]
                    row.update(answer=result["messages"][-1]["content"], tools=result["messages"][-1].get("toolTraceJson"),
                               usage=result.get("usage"), lastCallStats=result.get("stats"),
                               diagnostics=result["messages"][-1].get("diagnosticsJson"))
                    row["indicators"] = indicators(case, row["answer"])
                    if case.get("requireRecallPatterns") and not all(row["indicators"]["factPatternHits"]):
                        raise RuntimeError("Rappel historique attendu absent ; revue humaine nécessaire")
                    if row["usage"]:
                        identity = row["usage"].get("modelIdentity")
                        if identity:
                            if report["modelIdentity"] not in (None, identity):
                                raise RuntimeError("L’identité du modèle a changé pendant le run")
                            report["modelIdentity"] = identity
                except Exception as error:
                    row["error"] = str(error)
                finally:
                    row["elapsedSeconds"] = round(time.monotonic() - started, 3)
                    report["results"].append(row)
                    save()
                    if cid:
                        call("DELETE", "/conversations/" + cid, expected=204)
                        remaining.remove(cid)
                print(case["id"], repeat + 1, "ERREUR" if "error" in row else "réponse à examiner", flush=True)
    finally:
        for cid in remaining[:]:
            try:
                call("DELETE", "/conversations/" + cid, expected=204)
                remaining.remove(cid)
            except Exception:
                pass
        report["cleaned"] = not remaining
        report["remainingConversations"] = remaining
        save()
    if remaining or any("error" in r for r in report["results"]):
        raise SystemExit("Run incomplet : consulter run.json")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    v = sub.add_parser("validate")
    v.add_argument("--suite", type=Path, default=DATA)
    r = sub.add_parser("run")
    r.add_argument("--url", default="http://127.0.0.1:5187")
    r.add_argument("--suite", type=Path, default=DATA)
    r.add_argument("--model", required=True)
    r.add_argument("--label", required=True)
    r.add_argument("--output", type=Path, required=True)
    r.add_argument("--context", type=int, default=8192)
    r.add_argument("--output-tokens", type=int, default=1024)
    r.add_argument("--calls", type=int, default=12)
    r.add_argument("--retrieval", choices=("legacy", "lexical", "hybrid"))
    r.add_argument("--embedding-model")
    r.add_argument("--counter", choices=("auto", "conservative"))
    r.add_argument("--memory", choices=("structured", "recursive"))
    r.add_argument("--tools", action="store_true")
    r.add_argument("--seed", type=int, default=42)
    r.add_argument("--repeats", type=int, choices=range(1, 6), default=1)
    r.add_argument("--split", choices=("development", "holdout", "all"), default="development")
    c = sub.add_parser("compare")
    c.add_argument("baseline", type=Path)
    c.add_argument("candidate", type=Path)
    args = parser.parse_args()
    if args.action == "validate":
        files, cases, fingerprint = load_suite(args.suite)
        print(json.dumps({"files": len(files), "cases": len(cases), "sha256": fingerprint}))
    elif args.action == "run":
        run(args)
    else:
        left, right = json.loads(args.baseline.read_text(encoding="utf-8")), json.loads(args.candidate.read_text(encoding="utf-8"))
        if left.get("modelIdentity") and right.get("modelIdentity") and left["modelIdentity"] != right["modelIdentity"]:
            raise SystemExit("Identité de modèle différente")
        print(json.dumps(compare(left, right), indent=2, ensure_ascii=False))

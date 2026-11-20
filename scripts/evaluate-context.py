#!/usr/bin/env python3
"""Évaluation locale reproductible ; les indicateurs lexicaux nécessitent une revue humaine."""
import argparse
import hashlib
import importlib.util
import io
import json
import math
import re
import platform
import subprocess
import tempfile
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
            "humanReview": {"correctFacts": None, "missingFacts": None, "unsupportedClaims": None, "factualFidelity": None, "citationRelevance": None, "abstained": None, "notes": ""}}


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
            "mrr": next((1 / (i + 1) for i, hit in enumerate(hits) if hit), 0) if expected else None,
            "noAnswerCorrectlyEmpty": not ranked if not expected else None}


def distribution(values):
    values = sorted(v for v in values if isinstance(v, (int, float)) and math.isfinite(v))
    return {"n": len(values), **{name: values[math.ceil(len(values) * p) - 1] if values else None
                                 for name, p in (("p50", .5), ("p95", .95))}}


def summarize(rows):
    done = [r for r in rows if not r.get("error")]
    diagnostics = [json.loads(r["diagnostics"]) for r in done if r.get("diagnostics")]
    citations = [d["citations"] for d in diagnostics if d.get("citations")]
    refs = sum(len(c["recognized"]) + len(c["unknown"]) for c in citations)
    goals = [g for d in diagnostics for g in (d.get("research") or {}).get("goals", [])]
    calls = [c for r in done for c in (r.get("usage") or {}).get("calls", []) if c.get("purpose") in ("chat", "tools")]
    annotations = [(r.get("indicators") or {}).get("humanReview", {}) for r in done]
    def rate(values):
        values = [v for v in values if v is not None]
        return {"n": len(values), "mean": sum(values) / len(values) if values else None}
    return {"completed": len(done), "failed": len(rows) - len(done),
            "retrieval": {k: rate((r.get("retrieval") or {}).get(k) for r in done) for k in ("recall", "ndcg", "mrr")},
            "generationSeconds": distribution(r.get("generationSeconds") for r in done),
            "firstContentMs": distribution(c.get("firstContentMilliseconds") for c in calls),
            "estimatedPromptTokens": distribution(c.get("estimatedInputTokens") for c in calls),
            "providerPromptTokens": distribution((c.get("stats") or {}).get("promptTokens") for c in calls),
            "processRamBytes": distribution(c.get("apiWorkingSetBytes") for c in calls),
            "residentModelVramBytes": distribution(c.get("modelVramBytes") for c in calls),
            "subquestionEvidenceCoverage": rate(g["state"] in ("supplied", "passages_read") for g in goals),
            "recognizedReferenceFraction": sum(len(c["recognized"]) for c in citations) / refs if refs else None,
            "annotatedFactualFidelity": rate(a.get("factualFidelity") for a in annotations),
            "annotatedCitationRelevance": rate(a.get("citationRelevance") for a in annotations),
            "annotatedAbstention": rate(a.get("abstained") for a in annotations)}


def compare(left, right, single_factor=False):
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
    changed = set()
    for key in a:
        opts_a, opts_b = a[key].get("options") or {}, b[key].get("options") or {}
        changed.update(k for k in opts_a.keys() | opts_b.keys() if opts_a.get(k) != opts_b.get(k))
    if single_factor and len(changed) != 1:
        raise ValueError("Une ablation exige exactement une option différente : " + ", ".join(sorted(changed)))
    if left.get("hardware") != right.get("hardware"):
        raise ValueError("Matériel déclaré différent")
    return {"labels": [left["label"], right["label"]], "sameProtocol": True, "changedOptions": sorted(changed),
            "summaries": [summarize(list(a.values())), summarize(list(b.values()))],
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
              "split": args.split, "results": [], "cleaned": False, "hardware": args.hardware,
              "harnessPython": platform.python_version(), "hostPlatform": platform.platform()}
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
                    requested = json.loads(args.options.read_text(encoding="utf-8")) if args.options else {}
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
                    importing = time.monotonic()
                    call("POST", prefix + "/files/" + str(uuid.uuid4()), raw=body,
                         headers={"Content-Type": "multipart/form-data; boundary=" + boundary})
                    row["importMilliseconds"] = (time.monotonic() - importing) * 1000
                    if applied.get("retrievalMode") == "hybrid" and args.index_wait > 0:
                        call("POST", "/indexing", {"owner": cid, "project": False}, expected=202)
                        deadline = time.monotonic() + args.index_wait
                        while True:
                            jobs = call("GET", f"/indexing?owner={cid}&project=false")
                            if jobs and jobs[0]["state"] == "completed":
                                row["indexProgress"] = jobs[0]
                                break
                            if time.monotonic() >= deadline or jobs and jobs[0]["state"] in ("paused", "failed"):
                                raise RuntimeError("Indexation incomplète pendant la campagne")
                            time.sleep(1)
                    row["historyTurns"] = []
                    for turn in case.get("turns", []):
                        revision = call("GET", prefix + "/settings")["conversation"]["revision"]
                        events = call("POST", prefix + "/stream", {"requestId": str(uuid.uuid4()), "revision": revision, "content": turn})
                        if not events or events[-1]["type"] != "done":
                            raise RuntimeError("Historique interrompu avant la question de rappel")
                        row["historyTurns"].append(events[-1]["result"].get("usage"))
                    preparing = time.monotonic()
                    preview = call("POST", prefix + "/context", {"content": case["question"]})
                    row["previewMilliseconds"] = (time.monotonic() - preparing) * 1000
                    row["preview"] = preview["context"]
                    row["retrieval"] = retrieval_metrics(case, row["preview"].get("sources") or [])
                    row["previewUsage"] = preview.get("usage")
                    revision = call("GET", prefix + "/settings")["conversation"]["revision"]
                    generating = time.monotonic()
                    events = call("POST", prefix + "/stream", {"requestId": str(uuid.uuid4()), "revision": revision, "content": case["question"]})
                    row["generationSeconds"] = time.monotonic() - generating
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
                        row["embeddingIdentities"] = sorted({c["purpose"].split(":", 1)[1] for c in row["usage"].get("calls", [])
                                                            if c["purpose"].startswith("embedding-query:")})
                        diagnostic = json.loads(row["diagnostics"]) if row.get("diagnostics") else {}
                        row["rerankerIdentity"] = ((diagnostic.get("retrieval") or {}).get("rerank") or {}).get("identity")
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
        report["summary"] = summarize(report["results"])
        report["cleaned"] = not remaining
        report["remainingConversations"] = remaining
        save()
    if remaining or any("error" in r for r in report["results"]):
        raise SystemExit("Run incomplet : consulter run.json")


def offline_campaign(args):
    bench = ROOT / "benchmarks/Alycia.ContextBenchmarks"
    suite = ROOT / "evaluation/context-v3"
    factors = json.loads((suite / "candidate.json").read_text())
    if args.rerank:
        factors["rerankEnabled"] = True
    variants = {"reference": {}, **{k: {k: v} for k, v in factors.items()}, "combined": factors}
    command = ["dotnet", "run", "--no-build", "--project", str(bench), "--"]
    runs = {}
    with tempfile.TemporaryDirectory() as tmp:
        path = Path(tmp) / "options.json"
        for name, options in variants.items():
            path.write_text(json.dumps(options), encoding="utf-8")
            raw = subprocess.check_output(command + ["--offline", str(suite / "offline.json"), str(path)], cwd=ROOT, text=True)
            runs[name] = json.loads(raw)
            if options.get("rerankEnabled") and not any((row.get("rerank") or {}).get("state") == "applied" for row in runs[name]["rows"]):
                raise ValueError("Reclassement ONNX demandé mais non mesuré")
    baseline = runs["reference"]
    failures = []
    for label, run in runs.items():
        if (run["sha256"], run["k"], run["samples"]) != (baseline["sha256"], baseline["k"], baseline["samples"]):
            raise ValueError("Corpus ou protocole modifié")
        run["regressions"] = [a["id"] for a, b in zip(baseline["rows"], run["rows"], strict=True)
                              if a["id"] != b["id"] or any((a[k] or 0) > (b[k] or 0) for k in ("recall", "ndcg", "rr"))]
        if any(run[k] + 1e-12 < baseline[k] for k in ("meanRecall", "meanNdcg", "mrr")):
            failures.append(label)
    claims = json.loads(subprocess.check_output(command + ["--claims", str(suite / "claims.json")], cwd=ROOT, text=True))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as dest:
        json.dump({"schemaVersion": 1, "factors": factors, "runs": runs, "claims": claims, "failedThresholds": failures},
                  dest, ensure_ascii=False, indent=2)
        dest.write("\n")
    if failures:
        raise SystemExit("Régression sur un seuil préétabli : " + ", ".join(failures))


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
    r.add_argument("--options", type=Path, help="Surcharge JSON des options pour les campagnes par ablation")
    r.add_argument("--counter", choices=("auto", "conservative"))
    r.add_argument("--memory", choices=("structured", "recursive"))
    r.add_argument("--hardware", help="Description du matériel de l’API et d’Ollama pour cette campagne")
    r.add_argument("--index-wait", type=int, default=300, help="Attente maximale de l’index hybride en secondes (0 pour étudier une indexation à froid)")
    r.add_argument("--tools", action="store_true")
    r.add_argument("--seed", type=int, default=42)
    r.add_argument("--repeats", type=int, choices=range(1, 6), default=1)
    r.add_argument("--split", choices=("development", "holdout", "all"), default="development")
    c = sub.add_parser("compare")
    c.add_argument("baseline", type=Path)
    c.add_argument("candidate", type=Path)
    c.add_argument("--single-factor", action="store_true")
    offline = sub.add_parser("offline", help="Comparer la référence, les ablations et leur combinaison sur la recherche C#")
    offline.add_argument("--output", type=Path, required=True)
    offline.add_argument("--rerank", action="store_true", help="Exiger le reclassement ONNX avec les poids locaux de l’opérateur")
    args = parser.parse_args()
    if args.action == "validate":
        files, cases, fingerprint = load_suite(args.suite)
        print(json.dumps({"files": len(files), "cases": len(cases), "sha256": fingerprint}))
    elif args.action == "offline":
        offline_campaign(args)
    elif args.action == "run":
        run(args)
    else:
        left, right = json.loads(args.baseline.read_text(encoding="utf-8")), json.loads(args.candidate.read_text(encoding="utf-8"))
        if left.get("modelIdentity") and right.get("modelIdentity") and left["modelIdentity"] != right["modelIdentity"]:
            raise SystemExit("Identité de modèle différente")
        print(json.dumps(compare(left, right, args.single_factor), indent=2, ensure_ascii=False))

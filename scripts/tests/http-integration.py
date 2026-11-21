#!/usr/bin/env python3
"""API réelle + PostgreSQL isolé + Ollama simulé, sans GPU ni téléchargement de modèle."""
import argparse
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import threading
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]


class Ollama(BaseHTTPRequestHandler):
    chat_calls = 0
    summary_calls = 0
    tool_rounds = 0
    memory_calls = 0
    embedding_calls = 0
    pressure_active = False
    pressure_rounds = 0
    pressure_summaries = 0
    pressure_file = None
    pressure_protected = None
    turn_started = threading.Event()
    release_turn = threading.Event()

    def log_message(self, *_):
        pass

    def do_GET(self):
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(json.dumps({"models": [{"name": name, "digest": "fixture-" + name}
            for name in ("demo", "tool-demo", "embedding-alias", "remote:cloud")]}).encode())

    def do_POST(self):
        if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
            chunks = []
            while True:
                size = int(self.rfile.readline().split(b";", 1)[0], 16)
                if size == 0:
                    while self.rfile.readline().strip():
                        pass
                    break
                chunks.append(self.rfile.read(size))
                assert self.rfile.read(2) == b"\r\n"
            body = b"".join(chunks)
        else:
            body = self.rfile.read(int(self.headers["Content-Length"]))
        request = json.loads(body)
        if self.path == "/api/show":
            assert request["model"] in ("demo", "tool-demo", "embedding-alias")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            capabilities = ["completion", "tools"] if request["model"] == "tool-demo" else ["completion" if request["model"] == "demo" else "embedding"]
            self.wfile.write(json.dumps({"capabilities": capabilities}).encode())
            return
        if self.path == "/api/embed":
            assert request["model"] == "embedding-alias" and request["truncate"] is False
            Ollama.embedding_calls += 1
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(json.dumps({"embeddings": [[text.lower().count("answer") + 1, text.lower().count("other") + 1]
                for text in request["input"]], "prompt_eval_count": 30, "total_duration": 100000}).encode())
            return
        assert self.path == "/api/chat" and request["model"] in ("demo", "tool-demo"), "Un modèle non génératif a reçu une demande de chat"
        prompt = json.dumps(request["messages"], ensure_ascii=False)
        assert "NE_JAMAIS_RESUMER_CE_CONTENU" not in prompt, "Un contenu exclu a été envoyé au modèle"
        if isinstance(request.get("format"), dict) and "sourceUnitIds" in request["format"].get("properties", {}):
            text = request["messages"][-1]["content"]
            ids = json.loads(text.split("sourceUnitIds=", 1)[1].split("\n", 1)[0])
            labels = {key: [] for key in ("keywords", "topics", "entities", "technicalSymbols", "facts", "dateMentions", "uncertainties")}
            labels.update(title="Orion", description="", documentType="document", language="fr",
                          summary="Conservation Orion", keywords=["Orion"], sourceUnitIds=ids)
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(json.dumps({"message": {"role": "assistant", "content": json.dumps(labels)},
                                        "done": True, "done_reason": "stop", "eval_count": 50}).encode())
            return
        if request["messages"][-1]["content"].startswith("QA_ADVANCED"):
            import re
            marker = re.search(r"\[(S[0-9A-F]{12})\]", request["messages"][-1]["content"])
            assert marker and request["options"]["num_ctx"] < 8192 and request["options"]["num_predict"] == 256
            reply = {"message": {"role": "assistant", "content": f"La conservation Orion est de 79 jours [{marker[1]}]."},
                     "done": True, "done_reason": "stop", "eval_count": 12, "prompt_eval_count": 60}
            self.send_response(200)
            self.send_header("Content-Type", "application/x-ndjson")
            self.end_headers()
            self.wfile.write(json.dumps(reply).encode() + b"\n")
            return
        if request["messages"][-1]["content"].startswith("BOR_TEST_SNAPSHOT"):
            assert "INSTRUCTION_V1" in prompt and "INSTRUCTION_V2" not in prompt
            Ollama.turn_started.set()
            assert Ollama.release_turn.wait(20)
            self.send_response(200)
            self.send_header("Content-Type", "application/x-ndjson" if request.get("stream") else "application/json")
            self.end_headers()
            self.wfile.write(json.dumps({"message": {"role": "assistant", "content": "Révision initiale."}, "done": True, "done_reason": "stop"}).encode() + b"\n")
            return
        if Ollama.pressure_active:
            self.pressure_reply(request)
            return
        if request.get("tools"):
            assert request["model"] == "tool-demo" and request["stream"]
            assert {t["function"]["name"] for t in request["tools"]} == {"project_list", "project_read", "project_search", "project_search_passages", "project_find_symbols", "project_find_references", "project_read_many", "history_search", "history_read"}
            Ollama.tool_rounds += 1
            last = request["messages"][-1]
            if last.get("tool_name") == "project_read":
                assert "CODE_SENTINEL_END" in last["content"]
                reply = {"message": {"role": "assistant", "content": "CODE_SENTINEL_END retourne 42 dans src/Deep.cs. [" + json.loads(last["content"])["reference"] + "]"}, "done": True, "done_reason": "stop"}
            else:
                if last.get("tool_name") == "project_search":
                    hit = json.loads(last["content"])["matches"][0]
                    tool, args = "project_read", {"fileId": hit["fileId"], "offset": hit["offset"]}
                elif last.get("tool_name") == "project_list":
                    tool, args = "project_search", {"query": "CODE_SENTINEL_END"}
                else:
                    tool, args = "project_list", {"codeOnly": True, "limit": 1}
                reply = {"message": {"role": "assistant", "content": "", "thinking": "Consulter les sources.",
                    "tool_calls": [{"function": {"name": tool, "arguments": args}}]}, "done": True, "done_reason": "stop"}
            self.send_response(200)
            self.send_header("Content-Type", "application/x-ndjson")
            self.end_headers()
            self.wfile.write(json.dumps(reply).encode() + b"\n")
            return
        if request.get("stream"):
            assert request["options"]["num_predict"] == -1
            Ollama.chat_calls += 1
        else:
            assert request["options"]["num_predict"] > 0
            Ollama.summary_calls += 1
        self.send_response(200)
        self.send_header("Content-Type", "application/x-ndjson" if request.get("stream") else "application/json")
        self.end_headers()
        reply = {"message": {"role": "assistant", "content": "Réponse de recette."}, "done": True,
            "prompt_eval_count": 20, "eval_count": 5, "eval_duration": 100000000, "done_reason": "stop"}
        if request.get("format"):
            assert not request["stream"] and request["format"]["type"] == "object"
            Ollama.memory_calls += 1
            original = json.loads(request["messages"][-1]["content"])["messages"]
            reply["message"]["content"] = json.dumps({"entries": [{"sequence": m["sequence"],
                "kind": "constraint", "key": "budget", "quote": m["content"][:100]} for m in original]})
        if request.get("stream"):
            self.wfile.write(b'{"message":{"role":"assistant","thinking":"thinking","content":""},"done":false}\n')
            self.wfile.flush()
        self.wfile.write(json.dumps(reply).encode() + b"\n")

    def pressure_reply(self, request):
        if request.get("tools"):
            assert "think" not in request, "Le raisonnement conversationnel ne doit pas être désactivé"
            messages = request["messages"]
            assert messages[0]["content"].startswith("SYSTEM_PRESSURE")
            assert [m["content"] for m in messages[1:5]] == ["u1", "a1", "u2", "a2"]
            # Source excerpts are appended to the current request by ConversationContext.
            assert messages[5]["role"] == "user"
            assert messages[5]["content"].split("\n\n", 1)[0] == "PRESSURE_SENTINEL : examine le code."
            if Ollama.pressure_protected is None:
                Ollama.pressure_protected = messages[:6]
            assert messages[:6] == Ollama.pressure_protected
            offsets = [None, 0, 3000, 6000, 0]
            index = Ollama.pressure_rounds
            Ollama.pressure_rounds += 1
            if index < len(offsets):
                args = {"fileId": str(uuid.UUID(int=0)) if offsets[index] is None else Ollama.pressure_file,
                    "offset": offsets[index] or 0, "maxBytes": 5000}
                message = {"role": "assistant", "content": "", "tool_calls": [
                    {"function": {"name": "project_read", "arguments": args}}]}
            else:
                assert any("REPLI EXTRACTIF" in m["content"] for m in messages)
                last = json.loads(messages[-1]["content"])
                assert "reference" in last, "La relecture d’une source compactée a été refusée"
                message = {"role": "assistant", "content": "Analyse partielle du code. [" + last["reference"] + "]"}
            reply = {"message": message, "done": True, "done_reason": "stop", "prompt_eval_count": 2000, "eval_count": 20}
        else:
            Ollama.pressure_summaries += 1
            assert request["think"] is False
            assert 256 <= request["options"]["num_predict"] <= 1024
            reply = {"message": {"role": "assistant", "content": "", "thinking": "PRIVATE_THINKING"},
                "done": True, "done_reason": "length", "prompt_eval_count": 4000, "eval_count": 256}
        self.send_response(200)
        self.send_header("Content-Type", "application/x-ndjson" if request.get("stream") else "application/json")
        self.end_headers()
        self.wfile.write(json.dumps(reply).encode() + b"\n")


def verify_model_selection(port):
    def call(method, path, body=None, expected=200):
        client = http.client.HTTPConnection("127.0.0.1", port, timeout=30)
        try:
            client.request(method, "/api/chat" + path, json.dumps(body) if body is not None else None,
                {"Content-Type": "application/json"} if body is not None else {})
            response = client.getresponse()
            payload = response.read()
            assert response.status == expected, f"{method} {path}: {response.status}, attendu {expected}"
            if "application/x-ndjson" in response.getheader("Content-Type", ""):
                return [json.loads(line) for line in payload.splitlines() if line.strip()]
            return json.loads(payload) if payload else None
        finally:
            client.close()

    assert call("GET", "/models") == ["demo", "tool-demo"]
    call("POST", "/conversations", {"title": "Embedding refusé", "model": "embedding-alias", "systemPrompt": ""}, expected=400)
    options = {"contextTokens": 4096, "maxOutputTokens": 512}
    old = call("POST", "/import", {"schemaVersion": 1, "title": "Ancienne conversation embedding",
        "model": "embedding-alias", "systemPrompt": "", "contextNotes": "", "options": options, "messages": []}, expected=201)
    assert set(old) == {
        "id", "workspaceId", "title", "model", "systemPrompt", "createdAtUtc", "updatedAtUtc",
        "revision", "messageCount", "settingsJson", "contextNotes", "memoryText", "memoryThroughSequence",
        "memorySourceHash", "memoryModel", "memoryLedgerJson",
    }
    prefix = "/conversations/" + old["id"]
    request = {"requestId": str(uuid.uuid4()), "content": "Bonjour", "revision": old["revision"]}
    try:
        failure = call("POST", prefix + "/stream", request, expected=503)
        assert "embeddings" in failure["message"] and "Réglages" in failure["message"]
        assert call("GET", prefix + "/messages")["conversation"]["messageCount"] == 0
        assert Ollama.chat_calls == 0
        fixed = call("PUT", prefix + "/settings", {"revision": old["revision"], "model": "demo",
            "systemPrompt": "", "contextNotes": "", "options": options})
        assert fixed["id"] == old["id"] and fixed["model"] == "demo"
        events = call("POST", prefix + "/stream", dict(request, revision=fixed["revision"]))
        assert events[-1]["type"] == "done" and len(events[-1]["result"]["messages"]) == 2
        turn = events[-1]["result"]
        assert set(turn) == {"messages", "historyMessagesIncluded", "replayed", "filesIncluded", "estimatedInputTokens", "stats", "usage"}
        assert set(turn["messages"][1]) == {
            "conversationId", "sequence", "requestId", "role", "content", "createdAtUtc", "diagnosticsJson", "toolTraceJson",
        }
        stored = call("GET", prefix + "/messages")["messages"]
        assert len(stored) == len(turn["messages"])
        for fresh, row in zip(turn["messages"], stored):
            # The live model has 100 ns ticks; PostgreSQL stores timestamps to one microsecond.
            assert datetime.fromisoformat(fresh["createdAtUtc"]) == datetime.fromisoformat(row["createdAtUtc"])
            assert fresh == dict(row, createdAtUtc=fresh["createdAtUtc"])
    finally:
        call("DELETE", prefix, expected=204)


def verify_hybrid(call, checks):
    conv = call("POST", "/conversations", {"title": "Hybride V4", "model": "demo", "systemPrompt": "Réponds avec les sources."}, expected=201)
    prefix = "/conversations/" + conv["id"]
    try:
        settings = call("GET", prefix + "/settings")
        options = dict(settings["options"], useProjectTools=False, retrievalMode="hybrid", embeddingModel="embedding-alias")
        call("PUT", prefix + "/settings", {"revision": conv["revision"], "model": "demo",
            "systemPrompt": conv["systemPrompt"], "contextNotes": "", "options": options})
        boundary = "hybrid-" + uuid.uuid4().hex
        code = b"class Demo { int Answer() => 42; int Other() => 12; }"
        body = (f'--{boundary}\r\nContent-Disposition: form-data; name="files"; filename="Demo.cs"\r\n'
            'Content-Type: text/plain\r\n\r\n').encode() + code + f'\r\n--{boundary}--\r\n'.encode()
        call("POST", prefix + "/files/" + str(uuid.uuid4()), raw=body,
            headers={"Content-Type": "multipart/form-data; boundary=" + boundary})
        preview = call("POST", prefix + "/context", {"content": "Que fait Answer ?"})
        assert preview["context"]["retrieval"]["mode"] == "hybrid"
        assert preview["context"]["sources"] and preview["usage"]["inputTokens"] > 0
        assert len(preview["usage"]["calls"]) == 1, "Une demande ne doit encoder que sa requête"
        for _ in range(200):
            jobs = call("GET", f'/indexing?owner={conv["id"]}&project=false')
            if jobs and jobs[0]["state"] == "completed":
                break
            assert not jobs or jobs[0]["state"] != "failed", jobs
            time.sleep(0.1)
        else:
            raise AssertionError("L’indexation en arrière-plan n’a pas terminé")
        assert jobs[0]["completed"] == jobs[0]["total"] == 1
        preview = call("POST", prefix + "/context", {"content": "Que fait Answer ?"})
        cache = preview["context"]["retrieval"]["cache"]
        assert cache["vectorHits"] == preview["context"]["retrieval"]["candidateChunks"]
        call("POST", "/indexing/pause", {"owner": conv["id"], "project": False}, expected=204)
        assert call("GET", f'/indexing?owner={conv["id"]}&project=false')[0]["state"] == "paused"
        before = Ollama.embedding_calls
        cached = call("POST", prefix + "/context", {"content": "Que fait Answer ?"})
        assert Ollama.embedding_calls == before + 1 and len(cached["usage"]["calls"]) == 1
        assert cached["context"]["sources"] == preview["context"]["sources"]
        checks.append("background_indexing_progress_pause_and_query_only_embedding_cost")
    finally:
        call("DELETE", prefix, expected=204)


def verify_knowledge(call, checks):
    def create(title):
        return call("POST", "/conversations", {"title": title, "model": "demo", "systemPrompt": "Consulte les originaux."}, expected=201)

    owner, other = create("Synthèses indépendantes"), create("Portée distincte")
    prefix = "/conversations/" + owner["id"]
    try:
        options = call("GET", prefix + "/settings")["options"]
        options.update(enrichment=dict(options["enrichment"], enabled=True, model="demo"), facetPlanningEnabled=True,
                       classificationEnabled=True, evidenceSelectionEnabled=True, hierarchicalIndexEnabled=True,
                       maxOutputTokens=256, useProjectTools=False, querySummaryTokens=512)
        call("PUT", prefix + "/settings", {"revision": owner["revision"], "model": "demo",
             "systemPrompt": owner["systemPrompt"], "contextNotes": "", "options": options})
        boundary = "notes-" + uuid.uuid4().hex
        content = "# Orion\nLa conservation Orion vaut 37 jours."
        body = (f'--{boundary}\r\nContent-Disposition: form-data; name="files"; filename="orion.md"\r\n'
                f'Content-Type: text/plain\r\n\r\n{content}\r\n--{boundary}--\r\n').encode()
        call("POST", prefix + "/files/" + str(uuid.uuid4()), raw=body, headers={"Content-Type": "multipart/form-data; boundary=" + boundary})
        file = call("GET", prefix + "/files")[0]
        selection = f'project=false&file={file["id"]}'
        lookup = f'/indexing/knowledge?owner={owner["id"]}&{selection}'
        for _ in range(200):
            jobs = call("GET", f'/indexing?owner={owner["id"]}&project=false')
            if jobs and jobs[0]["state"] == "completed":
                break
            assert not jobs or jobs[0]["state"] != "failed", jobs
            time.sleep(.1)
        else:
            raise AssertionError("Synthèses non publiées")
        root = call("GET", lookup)
        assert root["level"] == "document" and root["state"] == "generated", root
        section = call("GET", lookup + "&unit=" + root["children"][0])
        leaf = call("GET", lookup + "&unit=" + section["children"][0])
        ref = leaf["labels"]["sourceUnitIds"][0]
        call("GET", f'/indexing/knowledge?owner={other["id"]}&{selection}&unit={leaf["key"]}', expected=404)
        question = "Combien de jours de conservation pour Orion ?"
        preview = call("POST", prefix + "/context", {"content": question})["context"]
        assert preview["retrieval"]["plan"]["brief"]["original"] == question
        assert preview["retrieval"]["classification"]["candidates"][0]["relevance"] == "relevant"
        assert preview["guides"][0]["originalReference"] == ref and preview["guides"][0]["tokens"] <= 512
        assert any(s["reference"] == ref for s in preview["sources"])
        revision = call("GET", prefix + "/settings")["conversation"]["revision"]
        call("PATCH", prefix + "/files/" + file["id"], {"revision": revision, "mode": "excluded"})
        call("GET", lookup, expected=404)
        preview = call("POST", prefix + "/context", {"content": question})["context"]
        assert not preview.get("sources") and not preview.get("guides")
        checks.extend(["independent_annotation_with_ollama_schema", "immutable_hierarchy_and_scope",
                       "facets_classification_and_final_source_ledger", "immediate_exclusion_of_derivatives"])
    finally:
        call("DELETE", prefix, expected=204)
        call("DELETE", "/conversations/" + other["id"], expected=204)


def verify_import_settings(call, checks):
    original = call("GET", "/import-settings")
    conv = call("POST", "/conversations", {"title": "Limites configurables", "model": "demo", "systemPrompt": ""}, expected=201)
    prefix = "/conversations/" + conv["id"]
    def upload(name, expected=200):
        boundary = "limits-" + uuid.uuid4().hex
        body = (f'--{boundary}\r\nContent-Disposition: form-data; name="files"; filename="{name}"\r\n'
            'Content-Type: text/plain\r\n\r\nTexte de test\r\n' + f'--{boundary}--\r\n').encode()
        return call("POST", prefix + "/files/" + str(uuid.uuid4()), raw=body,
            headers={"Content-Type": "multipart/form-data; boundary=" + boundary}, expected=expected)
    try:
        limits = dict(original, fileMiB=1, batchMiB=2, batchEntries=1, storedFiles=1, storedMiB=2)
        assert call("PUT", "/import-settings", limits) == limits
        assert call("GET", "/import-settings") == limits
        upload("first.txt")
        upload("second.txt", expected=400)
        assert len(call("GET", prefix + "/files")) == 1
        call("PUT", "/import-settings", dict(limits, storedFiles=0, storedMiB=0))
        upload("second.txt")
        assert len(call("GET", prefix + "/files")) == 2
        call("PUT", "/import-settings", dict(limits, fileMiB=0), expected=400)
        assert call("GET", "/import-settings")["storedFiles"] == 0
        checks.append("import_limits_persistence_enforcement_and_unlimited_scope")
    finally:
        call("PUT", "/import-settings", original)
        call("DELETE", prefix, expected=204)


def verify_structured_memory(call, checks):
    originals = [{"role": "user" if i % 2 == 0 else "assistant", "content": "Contrainte de budget. " + "texte " * 60,
        "createdAtUtc": "2026-11-17T00:00:00Z"} for i in range(40)]
    conv = call("POST", "/import", {"schemaVersion": 1, "title": "Mémoire structurée V4", "model": "demo",
        "systemPrompt": "SYSTEM_PROTECTED", "contextNotes": "", "options": {"contextTokens": 4096,
        "maxOutputTokens": 512, "useProjectTools": False, "structuredMemory": True}, "messages": originals}, expected=201)
    prefix = "/conversations/" + conv["id"]
    try:
        request = {"revision": conv["revision"], "content": "Quel budget ?"}
        preview = call("POST", prefix + "/context", request)
        assert preview["context"]["needsSummary"] and Ollama.memory_calls == 0
        events = call("POST", prefix + "/context/prepare", request)
        assert events[-1]["type"] == "done", events[-1]
        prepared = events[-1]["result"]
        assert prepared["context"]["memory"]["through"] == 36 and Ollama.memory_calls > 0
        assert all(c["purpose"] == "memory" for c in prepared["usage"]["calls"])
        stored = call("GET", prefix + "/settings")["conversation"]
        ledger = json.loads(stored["memoryLedgerJson"])
        assert ledger["through"] == 36 and ledger["fallbackSegments"] == 0
        for entry in ledger["entries"]:
            assert entry["quote"] in originals[entry["sequence"] - 1]["content"]
        before = Ollama.memory_calls
        cached = call("POST", prefix + "/context/prepare", dict(request, revision=stored["revision"]))[-1]
        assert cached["type"] == "done" and Ollama.memory_calls == before
        assert cached["result"]["context"]["messages"][0]["content"] == "SYSTEM_PROTECTED"
        assert [m["content"] for m in cached["result"]["context"]["messages"][-5:-1]] == [m["content"] for m in originals[-4:]]
        cleared = call("POST", prefix + "/memory/reset", {"revision": stored["revision"]})
        assert cleared["memoryLedgerJson"] == "{}" and cleared["messageCount"] == 40
        checks.append("structured_memory_quotes_provenance_cache_reset_and_protection")
    finally:
        call("DELETE", prefix, expected=204)


def verify_empty_tool_summary(call, checks):
    originals = [{"role": role, "content": content, "createdAtUtc": "2026-11-17T00:00:00Z"}
        for role, content in [("user", "u1"), ("assistant", "a1"), ("user", "u2"), ("assistant", "a2")]]
    conv = call("POST", "/import", {"schemaVersion": 1, "title": "Reprise outils V4", "model": "tool-demo",
        "systemPrompt": "SYSTEM_PRESSURE", "contextNotes": "", "options": {"contextTokens": 8192,
        "maxOutputTokens": 1024, "useProjectTools": True}, "messages": originals}, expected=201)
    prefix = "/conversations/" + conv["id"]
    try:
        boundary = "pressure-" + uuid.uuid4().hex
        code = ("// Source de recette pour lecture récursive.\n" * 400).encode()
        body = (f'--{boundary}\r\nContent-Disposition: form-data; name="files"; filename="Source.cs"\r\n'
            'Content-Type: text/plain\r\n\r\n').encode() + code + f'\r\n--{boundary}--\r\n'.encode()
        files = call("POST", prefix + "/files/" + str(uuid.uuid4()), raw=body,
            headers={"Content-Type": "multipart/form-data; boundary=" + boundary})
        Ollama.pressure_file = files[0]["id"]
        Ollama.pressure_active = True
        revision = call("GET", prefix + "/settings")["conversation"]["revision"]
        request = {"requestId": str(uuid.uuid4()), "revision": revision, "content": "PRESSURE_SENTINEL : examine le code."}
        events = call("POST", prefix + "/stream", request)
        assert events[-1]["type"] == "done", events[-1]
        result = events[-1]["result"]
        assert Ollama.pressure_summaries == 1 and Ollama.pressure_rounds == 6
        assistant = result["messages"][-1]
        trace = json.loads(assistant["toolTraceJson"])
        assert not trace[0]["succeeded"] and trace[0]["error"], "Le motif du refus manque"
        assert all(t["succeeded"] and not t["repeatSuppressed"] for t in trace[1:])
        diagnostic = json.loads(assistant["diagnosticsJson"])
        assert len(diagnostic["warnings"]) == 1
        assert "extraits partiels" in diagnostic["warnings"][0]
        usage = diagnostic["usage"]
        assert usage["complete"] and usage["outputTokens"] == 6 * 20 + 256
        summary = [c for c in usage["calls"] if c["purpose"] == "summary"]
        assert len(summary) == 1 and summary[0]["stats"]["doneReason"] == "length"
        assert "PRIVATE_THINKING" not in json.dumps(events)
        exported = call("GET", prefix + "/export?format=json")
        assert len(exported["messages"]) == 6
        assert [m["content"] for m in exported["messages"][:4]] == [m["content"] for m in originals]
        assert json.loads(exported["messages"][-1]["diagnosticsJson"]) == diagnostic
        assert call("POST", prefix + "/stream", request)[-1]["result"]["replayed"]
        assert Ollama.pressure_rounds == 6 and Ollama.pressure_summaries == 1
        checks.append("empty_tool_summary_recovery_warning_metrics_reread_persistence_and_replay")
    finally:
        Ollama.pressure_active = False
        call("DELETE", prefix, expected=204)


def verify_project_conflicts(port, call, checks):
    def race(method, path, left, right):
        ready = threading.Barrier(2, timeout=10)
        headers = {"Content-Type": "application/json"}
        if token := os.environ.get("ALYCIA_CHAT_ACCESS_TOKEN"):
            headers["Authorization"] = "Bearer " + token

        def send(body):
            connection = http.client.HTTPConnection("127.0.0.1", port, timeout=30)
            try:
                connection.connect()
                ready.wait()
                connection.request(method, "/api/chat" + path, json.dumps(body), headers)
                response = connection.getresponse()
                return response.status, json.load(response)
            finally:
                connection.close()

        with ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(send, (left, right)))
        return sorted(results, key=lambda result: result[0])

    name = "Créations concurrentes"
    success, conflict = race("POST", "/projects", {"name": name}, {"name": name})
    assert success[0] == 200 and conflict[0] == 400, (success, conflict)
    assert conflict[1]["code"] == "INVALID_REQUEST"
    project = success[1]
    assert [p["id"] for p in call("GET", "/projects") if p["name"] == name] == [project["id"]]
    checks.append("concurrent_project_creation_returns_one_identity_and_one_refusal")

    path = "/projects/" + project["id"]
    profile = call("GET", path)
    success, conflict = race("PUT", path, dict(profile, instructions="Première"), dict(profile, instructions="Deuxième"))
    assert success[0] == 200 and conflict[0] == 409, (success, conflict)
    assert conflict[1]["code"] == "CONVERSATION_CHANGED"
    assert success[1]["revision"] == profile["revision"] + 1
    assert success[1]["instructions"] in ("Première", "Deuxième")
    assert call("GET", path) == success[1]
    checks.append("concurrent_profile_edits_persist_only_the_winning_revision")


def verify_boreale(call, checks):
    project = call("POST", "/projects", {"name": "Recette boréale"})
    rejected = call("POST", "/projects", {"name": "Recette boréale"}, expected=400)
    assert rejected["code"] == "INVALID_REQUEST"
    assert [p["id"] for p in call("GET", "/projects") if p["name"] == "Recette boréale"] == [project["id"]]
    checks.append("duplicate_project_rejected_without_second_identity")
    other = call("POST", "/projects", {"name": "Autre projet"})
    base = "/projects/" + project["id"]
    profile = call("GET", base)
    profile = call("PUT", base, dict(profile, instructions="INSTRUCTION_V1", defaultModel="demo"))
    assert project["id"] in {p["id"] for p in call("GET", "/projects")}

    def create(label, wid):
        return "/conversations/" + call("POST", "/conversations", {"title": label, "model": "demo",
            "systemPrompt": "", "workspaceId": wid}, expected=201)["id"]

    first, second, stranger, outside = [create("Recette " + title, owner) for title, owner in (
        ("A", project["id"]), ("B", project["id"]), ("C", other["id"]), ("D", None))]
    boundary = "boreal-test"
    payload = (f'--{boundary}\r\nContent-Disposition: form-data; name="files"; filename="boreale.txt"\r\n'
               f'Content-Type: text/plain\r\n\r\nBOREAL_SENTINEL\r\n--{boundary}--\r\n').encode()
    file = call("POST", base + "/files/" + str(uuid.uuid4()), raw=payload,
                headers={"Content-Type": "multipart/form-data; boundary=" + boundary})[0]
    fid = file["id"]
    profile = call("GET", base)
    call("PUT", base + "/files/" + fid, {"mode": "pinned", "revision": profile["revision"]})
    for member in (first, second):
        assert fid in {f["id"] for f in call("GET", member + "/files")}
        passage = call("GET", member + "/files/" + fid + "/preview?sha=" + file["sha256"] + "&start=0&end=6")
        assert passage["text"] == "BOREAL"
    def tool_read(conversation):
        return call("POST", conversation + "/mcp", {"jsonrpc": "2.0", "id": 1, "method": "tools/call",
            "params": {"name": "project_read", "arguments": {"fileId": fid, "offset": 0}}},
            headers={"Accept": "application/json, text/event-stream", "MCP-Protocol-Version": "2025-11-25"})["result"]

    assert "BOREAL_SENTINEL" in str(tool_read(second)["content"])
    for isolated in (stranger, outside):
        assert tool_read(isolated)["isError"]
        assert not call("GET", isolated + "/files")
        call("GET", isolated + "/files/" + fid + "/preview", expected=404)
        assert "BOREAL_SENTINEL" not in str(call("POST", isolated + "/context", {"content": "boreale.txt"}))
    assert len(call("GET", "/search?workspaceId=" + project["id"])["items"]) == 2
    assert "BOREAL_SENTINEL" in str(call("POST", first + "/context", {"content": "boreale.txt"}))
    rev = call("GET", first + "/settings")["conversation"]["revision"]
    call("PATCH", first + "/files/" + fid, {"mode": "excluded", "revision": rev})
    assert "BOREAL_SENTINEL" not in str(call("POST", first + "/context", {"content": "boreale.txt"}))
    assert tool_read(first)["isError"]
    assert "BOREAL_SENTINEL" in str(call("POST", second + "/context", {"content": "boreale.txt"}))
    checks.append("project_source_membership_preview_search_and_local_exclusion")

    old = call("GET", base)
    revision = call("GET", second + "/settings")["conversation"]["revision"]
    with ThreadPoolExecutor(max_workers=1) as pool:
        pending = pool.submit(call, "POST", second + "/stream", {"requestId": str(uuid.uuid4()), "content": "BOR_TEST_SNAPSHOT", "revision": revision})
        try:
            assert Ollama.turn_started.wait(15), "Le modèle simulé n’a pas reçu la demande."
            updated = call("PUT", base, dict(old, instructions="INSTRUCTION_V2"))
        finally:
            Ollama.release_turn.set()
        events = pending.result(timeout=20)
        assert events[-1]["type"] == "done", events[-1]
    saved = call("GET", second + "/messages")["messages"][-1]
    assert json.loads(saved["diagnosticsJson"])["workspaceRevision"] == old["revision"]
    assert "INSTRUCTION_V2" in str(call("POST", second + "/context", {"content": "Suite"}))
    call("PUT", base, dict(old, instructions="Périmée"), expected=409)
    note = call("POST", base + "/notes", {"text": "NOTE_VALIDEE", "revision": updated["revision"],
                "originConversationId": second.rsplit("/", 1)[1], "originSequence": saved["sequence"]})
    assert note["notes"][0]["originSequence"] == saved["sequence"]
    assert "NOTE_VALIDEE" in str(call("POST", first + "/context", {"content": "Suite"}))
    assert "NOTE_VALIDEE" not in str(call("POST", stranger + "/context", {"content": "Suite"}))
    checks.append("project_snapshot_optimistic_revision_and_explicit_notes")

    call("DELETE", first, expected=204)
    assert fid in {f["id"] for f in call("GET", second + "/files")}
    profile = call("GET", base)
    call("DELETE", base + "/files/" + fid + "?revision=" + str(profile["revision"]), expected=204)
    assert not call("GET", second + "/files")
    assert call("GET", second + "/messages")["messages"][-1]["content"] == saved["content"]
    checks.append("project_source_survives_chat_then_explicit_withdrawal_preserves_history")


def verify_context_quality(call, checks):
    conv = call("POST", "/conversations", {"title": "Contextualisation", "model": "demo", "systemPrompt": "Consulte les originaux."}, expected=201)
    prefix = "/conversations/" + conv["id"]
    try:
        settings = call("GET", prefix + "/settings")
        options = dict(settings["options"], routingMode="auto", queryRewriteEnabled=True, evidenceSelectionEnabled=True,
                       rerankEnabled=True, hierarchicalIndexEnabled=True, temporalMemoryEnabled=True, claimVerificationEnabled=True,
                       verificationSearchEnabled=True, adaptiveInferenceEnabled=True, generateUntilStop=False,
                       maxOutputTokens=256, useProjectTools=False)
        call("PUT", prefix + "/settings", {"revision": conv["revision"], "model": "demo",
             "systemPrompt": conv["systemPrompt"], "contextNotes": "", "options": options})
        boundary = "qc-" + uuid.uuid4().hex
        body = (f'--{boundary}\r\nContent-Disposition: form-data; name="files"; filename="orion.md"\r\n'
                'Content-Type: text/plain\r\n\r\n# Orion\nLa conservation Orion est de 37 jours.\r\n'
                f'--{boundary}--\r\n').encode()
        call("POST", prefix + "/files/" + str(uuid.uuid4()), raw=body, headers={"Content-Type": "multipart/form-data; boundary=" + boundary})
        question = "QA_ADVANCED : lis orion.md et précise la conservation Orion."
        preview = call("POST", prefix + "/context", {"content": question})["context"]
        assert preview["route"]["search"] and preview["assembly"]["used"] <= preview["assembly"]["budget"]
        assert preview["retrieval"]["plan"]["original"] == question
        assert preview["retrieval"]["rerank"]["state"] == "unavailable"
        request_id = str(uuid.uuid4())
        revision = call("GET", prefix + "/settings")["conversation"]["revision"]
        request = {"requestId": request_id, "revision": revision, "content": question}
        events = call("POST", prefix + "/stream", request)
        assert events[-1]["type"] == "done", events[-1]
        assistant = events[-1]["result"]["messages"][-1]
        assert assistant["content"] == "".join(e.get("content") or "" for e in events if e["type"] == "delta")
        assert "Vérification après génération" in assistant["content"]
        diagnostics = json.loads(assistant["diagnosticsJson"])
        assert diagnostics["claims"]["findings"][0]["status"] == "contradicted"
        assert diagnostics["verification"]["status"] == "supplementary_contradiction"
        assert 0 < diagnostics["usage"]["calls"][-1]["effectiveContextTokens"] < options["contextTokens"]
        replay = call("POST", prefix + "/stream", request)[-1]["result"]
        assert replay["replayed"] and replay["messages"][-1]["content"] == assistant["content"]
        checks.extend(["scope_preview_and_send", "onnx_fallback", "streamed_contradiction_and_bounded_second_search",
                       "adaptive_ollama_payload", "quality_turn_idempotency"])
    finally:
        call("DELETE", prefix, expected=204)


def verify_originals(call, directory, checks):
    owner = call("POST", "/conversations", {"title": "Source", "model": "demo"}, expected=201)["id"]
    outsider = call("POST", "/conversations", {"title": "Voisine", "model": "demo"}, expected=201)["id"]
    prefix = "/conversations/" + owner
    data = b"0123456789"
    body = b'--part\r\nContent-Disposition: form-data; name="files"; filename="note.txt"\r\n\r\n' + data + b"\r\n--part--\r\n"
    file = call("POST", prefix + "/files/" + str(uuid.uuid4()), raw=body,
                headers={"Content-Type": "multipart/form-data; boundary=part"})[0]["id"]
    resource = prefix + "/files/" + file
    header = {"Range": "bytes=2-5"}
    assert call("GET", resource, headers=header, expected=206) == b"2345"
    assert call("GET", "/conversations/" + outsider + "/files/" + file, headers=header, expected=404)
    original = directory / uuid.UUID(owner).hex / (uuid.UUID(file).hex + ".bin")
    original.write_bytes(b"abcdefghij")
    call("GET", resource, headers=header, expected=503)
    original.write_bytes(data)
    revision = call("GET", prefix + "/settings")["conversation"]["revision"]
    call("PATCH", resource, {"revision": revision, "mode": "excluded"})
    call("GET", resource, headers=header, expected=404)
    checks.extend(["original_range_bytes", "original_owner_scope", "original_same_size_hash_mismatch", "original_range_after_revocation"])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--case", choices=("all", "transport", "tool-summary", "boreale", "indexing", "context-quality", "knowledge", "originals"), default="all")
    args = parser.parse_args()
    connection = os.environ.get("ALYCIA_CHAT_TEST_CONNECTION")
    if not connection:
        raise SystemExit("ALYCIA_CHAT_TEST_CONNECTION requis (administrateur de test avec CREATEDB).")
    dll = ROOT / "src/Alycia.Api/bin/Debug/net10.0/Alycia.Api.dll"
    if not dll.exists():
        raise SystemExit("Exécutez dotnet build Alycia.slnx avant cette recette.")
    database = "alycia_http_test_" + uuid.uuid4().hex
    env = dict(os.environ, ALYCIA_CHAT_CONNECTION=connection.rstrip(";") + ";Database=" + database,
        ALYCIA_CHAT_CONTAINER_MODE="false", ALYCIA_ENABLE_LOCAL_WORKSPACES="false",
        ALYCIA_ENABLE_LOCAL_CHAT="true", ASPNETCORE_ENVIRONMENT="Development", DOTNET_ENVIRONMENT="Development")
    env["ALYCIA_WORKSPACES_CONNECTION"] = env["ALYCIA_CHAT_CONNECTION"]
    # This scenario exercises the fallback independently of any locally installed weights.
    env.pop("ALYCIA_RERANK_DIRECTORY", None)
    ef = ["dotnet", "tool", "run", "dotnet-ef", "database"]
    ef_options = ["--project", "src/Alycia.Modules.Chat.Infrastructure", "--context", "ChatDbContext", "--no-build"]
    provider = ThreadingHTTPServer(("127.0.0.1", 0), Ollama)
    threading.Thread(target=provider.serve_forever, daemon=True).start()
    env["ALYCIA_OLLAMA_BASE_URL"] = f"http://127.0.0.1:{provider.server_port}/"
    backend = None
    created = False
    with tempfile.TemporaryDirectory(prefix="alycia-http-") as directory:
        env["ALYCIA_FILES_DIRECTORY"] = str(Path(directory) / "files")
        env["ALYCIA_LOG_PATH"] = str(Path(directory) / "api-.jsonl")
        with open(Path(directory) / "process.log", "w+") as log:
            try:
                # Mark cleanup before migration: a failed migration can still create the database.
                created = True
                subprocess.run(ef + ["update", "--project", "src/Alycia.Modules.Workspaces.Infrastructure",
                    "--context", "WorkspacesDbContext", "--no-build"], cwd=ROOT, env=env, check=True, stdout=log, stderr=log, timeout=120)
                subprocess.run(ef + ["update"] + ef_options, cwd=ROOT, env=env, check=True, stdout=log, stderr=log, timeout=120)
                with socket.socket() as reserve:
                    reserve.bind(("127.0.0.1", 0))
                    port = reserve.getsockname()[1]
                backend = subprocess.Popen(["dotnet", str(dll), "--urls", f"http://127.0.0.1:{port}"],
                    cwd=directory, env=env, stdout=log, stderr=log)
                for _ in range(100):
                    if backend.poll() is not None:
                        raise RuntimeError("Le backend de recette s’est arrêté au démarrage.")
                    client = http.client.HTTPConnection("127.0.0.1", port, timeout=1)
                    try:
                        client.request("GET", "/healthz")
                        if client.getresponse().status == 200:
                            break
                    except OSError:
                        time.sleep(0.1)
                    finally:
                        client.close()
                else:
                    raise RuntimeError("Backend de recette non prêt.")
                spec = importlib.util.spec_from_file_location("verify_local", ROOT / "scripts/verify-local.py")
                module = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(module)
                checks = []
                client = module.create_client(f"http://127.0.0.1:{port}")
                if args.case == "originals":
                    verify_originals(client, Path(env["ALYCIA_FILES_DIRECTORY"]), checks)
                if args.case in ("all", "transport"):
                    verify_model_selection(port)
                    checks.append("model_capabilities_and_existing_conversation_repair")
                if args.case == "all":
                    checks.extend(module.verify(f"http://127.0.0.1:{port}", "demo", extended=True))
                    assert Ollama.chat_calls == 3 and Ollama.summary_calls >= 5
                    owned = []
                    try:
                        summaries_before_tools = Ollama.summary_calls
                        module.verify_tools(client, "tool-demo", owned, checks)
                        assert Ollama.summary_calls == summaries_before_tools, "Les petits résultats d’outils ont été résumés inutilement"
                        assert Ollama.tool_rounds == 4, "Le renvoi a réexécuté les outils"
                    finally:
                        for conversation in owned:
                            client("DELETE", "/conversations/" + conversation, expected=204)
                    verify_structured_memory(client, checks)
                    checks.append("tool_citations_and_persisted_diagnostics")
                if args.case in ("all", "knowledge"):
                    verify_knowledge(client, checks)
                if args.case in ("all", "context-quality"):
                    verify_context_quality(client, checks)
                if args.case in ("all", "indexing"):
                    verify_hybrid(client, checks)
                    verify_import_settings(client, checks)
                if args.case in ("all", "tool-summary"):
                    verify_empty_tool_summary(client, checks)
                if args.case in ("all", "boreale"):
                    verify_project_conflicts(port, client, checks)
                    verify_boreale(client, checks)
                print(json.dumps({"passed": len(checks), "checks": checks, "provider": "mock",
                    "database": "isolated PostgreSQL", "test_data_cleaned": True,
                    "continuous_chat_calls": Ollama.chat_calls, "tool_rounds": Ollama.tool_rounds, "bounded_summary_calls": Ollama.summary_calls,
                    "pressure_tool_rounds": Ollama.pressure_rounds, "empty_tool_summaries": Ollama.pressure_summaries}, indent=2))
            except Exception:
                log.flush()
                log.seek(0)
                # Logs are written only for this synthetic test; redact connection strings if present.
                details = log.read().replace(connection, "[test connection]").replace(env["ALYCIA_CHAT_CONNECTION"], "[test connection]")
                print(details[-10000:])
                raise
            finally:
                if backend is not None:
                    backend.terminate()
                    try:
                        backend.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        backend.kill()
                        backend.wait()
                provider.shutdown()
                provider.server_close()
                if created:
                    subprocess.run(ef + ["drop", "--force"] + ef_options, cwd=ROOT, env=env,
                        check=True, stdout=log, stderr=log, timeout=60)


if __name__ == "__main__":
    main()

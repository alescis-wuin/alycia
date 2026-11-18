#!/usr/bin/env python3
"""API réelle + PostgreSQL isolé + Ollama simulé, sans GPU ni téléchargement de modèle."""
import argparse
from concurrent.futures import ThreadPoolExecutor
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
        assert len(preview["usage"]["calls"]) >= 2
        before = Ollama.embedding_calls
        cached = call("POST", prefix + "/context", {"content": "Que fait Answer ?"})
        assert Ollama.embedding_calls == before + 1 and len(cached["usage"]["calls"]) == 1
        assert cached["context"]["sources"] == preview["context"]["sources"]
        checks.append("hybrid_local_embedding_http_cache_and_accounted_preview_cost")
    finally:
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


def verify_boreale(call, checks):
    project = call("POST", "/projects", {"name": "Recette boréale"})
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
            "params": {"name": "project_read", "arguments": {"fileId": fid, "offset": 0}}})["result"]

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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--case", choices=("all", "tool-summary", "boreale"), default="all")
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
                if args.case == "all":
                    verify_model_selection(port)
                    checks = module.verify(f"http://127.0.0.1:{port}", "demo", extended=True)
                    checks.insert(0, "model_capabilities_and_existing_conversation_repair")
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
                    verify_hybrid(client, checks)
                    checks.append("tool_citations_and_persisted_diagnostics")
                if args.case in ("all", "tool-summary"):
                    verify_empty_tool_summary(client, checks)
                if args.case in ("all", "boreale"):
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

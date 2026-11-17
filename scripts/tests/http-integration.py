#!/usr/bin/env python3
"""API réelle + PostgreSQL isolé + Ollama simulé, sans GPU ni téléchargement de modèle."""
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

    def log_message(self, *_):
        pass

    def do_GET(self):
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(b'{"models":[{"name":"demo"},{"name":"tool-demo"},{"name":"embedding-alias"},{"name":"remote:cloud"}]}')

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
        assert self.path == "/api/chat" and request["model"] in ("demo", "tool-demo"), "Un modèle non génératif a reçu une demande de chat"
        prompt = json.dumps(request["messages"], ensure_ascii=False)
        assert "NE_JAMAIS_RESUMER_CE_CONTENU" not in prompt, "Un contenu exclu a été envoyé au modèle"
        if request.get("tools"):
            assert request["model"] == "tool-demo" and request["stream"]
            assert {t["function"]["name"] for t in request["tools"]} == {"project_list", "project_read", "project_search"}
            Ollama.tool_rounds += 1
            last = request["messages"][-1]
            if last.get("tool_name") == "project_read":
                assert "CODE_SENTINEL_END" in last["content"]
                reply = {"message": {"role": "assistant", "content": "CODE_SENTINEL_END retourne 42 dans src/Deep.cs."}, "done": True, "done_reason": "stop"}
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
        if request.get("stream"):
            self.wfile.write(b'{"message":{"role":"assistant","thinking":"thinking","content":""},"done":false}\n')
            self.wfile.flush()
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


def main():
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
                verify_model_selection(port)
                checks = module.verify(f"http://127.0.0.1:{port}", "demo", extended=True)
                checks.insert(0, "model_capabilities_and_existing_conversation_repair")
                assert Ollama.chat_calls == 3 and Ollama.summary_calls >= 5
                client = module.create_client(f"http://127.0.0.1:{port}")
                owned = []
                try:
                    module.verify_tools(client, "tool-demo", owned, checks)
                    assert Ollama.tool_rounds == 4, "Le renvoi a réexécuté les outils"
                finally:
                    for conversation in owned:
                        client("DELETE", "/conversations/" + conversation, expected=204)
                print(json.dumps({"passed": len(checks), "checks": checks, "provider": "mock",
                    "database": "isolated PostgreSQL", "test_data_cleaned": True,
                    "continuous_chat_calls": Ollama.chat_calls, "tool_rounds": Ollama.tool_rounds, "bounded_summary_calls": Ollama.summary_calls}, indent=2))
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

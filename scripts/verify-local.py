#!/usr/bin/env python3
"""Recette HTTP réelle. Crée puis supprime uniquement ses propres données de test."""
import argparse
import http.client
import hashlib
import io
import json
import os
import uuid
import zipfile
from urllib.parse import quote, urlsplit


def create_client(base_url):
    origin = urlsplit(base_url)
    if origin.scheme not in ("http", "https") or origin.hostname not in ("localhost", "127.0.0.1", "::1"):
        raise ValueError("Le backend doit être local.")
    if origin.username or origin.password or origin.query or origin.fragment or origin.path not in ("", "/"):
        raise ValueError("Origine locale invalide.")
    token = os.environ.get("ALYCIA_CHAT_ACCESS_TOKEN")

    def call(method, path, value=None, *, raw=None, headers=None, expected=200):
        connection_type = http.client.HTTPSConnection if origin.scheme == "https" else http.client.HTTPConnection
        connection = connection_type(origin.hostname, origin.port, timeout=660)
        request_headers = dict(headers or {})
        if token:
            request_headers["Authorization"] = "Bearer " + token
        if value is not None:
            raw = json.dumps(value, ensure_ascii=False).encode()
            request_headers["Content-Type"] = "application/json"
        try:
            connection.request(method, "/api/chat" + path, raw, request_headers)
            response = connection.getresponse()
            data = response.read()
            if response.status != expected:
                raise AssertionError(f"{method} {path}: HTTP {response.status}, attendu {expected}")
            if "application/x-ndjson" in response.getheader("Content-Type", ""):
                return [json.loads(line) for line in data.splitlines() if line.strip()]
            if "application/json" in response.getheader("Content-Type", "") and data:
                return json.loads(data)
            return data
        finally:
            connection.close()

    return call


def verify(base_url, model, *, extended=False, tools=False):
    conversations, prompts, checks = [], [], []
    call = create_client(base_url)

    def done(events):
        assert events and events[-1]["type"] == "done", "Absence de confirmation de persistance"
        return events[-1]["result"]

    try:
        assert model in call("GET", "/models"), "Modèle local absent de la liste Ollama"
        call("GET", "/models", headers={"Origin": "https://example.invalid"}, expected=403)
        call("GET", "/models", headers={"Host": "example.invalid"}, expected=403)
        checks.append("local_access_and_models")
        title = "Recette %_ " + uuid.uuid4().hex[:12]
        conversation = call("POST", "/conversations", {"title": title, "model": model,
            "systemPrompt": "Réponds brièvement en français."}, expected=201)
        cid = conversation["id"]
        conversations.append(cid)
        prefix = "/conversations/" + cid
        settings = call("GET", prefix + "/settings")
        options = settings["options"]
        options.update(contextTokens=4096, maxOutputTokens=512, temperature=0.1, maxHistoryTurns=8, useProjectTools=False)
        configured = call("PUT", prefix + "/settings", {"revision": conversation["revision"], "model": model,
            "systemPrompt": conversation["systemPrompt"], "contextNotes": "Projet de recette locale.", "options": options})
        stale = {"revision": conversation["revision"], "model": model, "systemPrompt": "", "options": options}
        call("PUT", prefix + "/settings", stale, expected=409)
        prompt = call("POST", "/prompts", {"name": title, "systemPrompt": "Prompt de recette"}, expected=201)
        prompts.append(prompt["id"])
        call("PUT", "/prompts/" + prompt["id"], {"name": title, "systemPrompt": "Prompt modifié"})
        assert any(p["id"] == prompt["id"] for p in call("GET", "/prompts"))
        checks.append("settings_revision_and_prompt_library")

        request = {"requestId": str(uuid.uuid4()), "content": "Réponds en une phrase : à quoi sert un test ?",
            "revision": configured["revision"]}
        events = call("POST", prefix + "/stream", request)
        result = done(events)
        assert any(event["type"] == "delta" for event in events)
        assert len(result["messages"]) == 2 and not result["replayed"]
        assert done(call("POST", prefix + "/stream", request))["replayed"]
        call("POST", prefix + "/stream", dict(request, content="autre texte"), expected=409)
        page = call("GET", prefix + "/messages")
        assert page["conversation"]["messageCount"] == 2
        checks.append("stream_commit_replay_and_conflict")

        boundary = "alycia-" + uuid.uuid4().hex
        content = b"DOCUMENT_DE_RECETTE"
        multipart = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"files\"; filename=\"Projet test/./notes.txt\"\r\n"
            "Content-Type: text/plain\r\n\r\n").encode() + content + f"\r\n--{boundary}--\r\n".encode()
        files = call("POST", prefix + "/files/" + str(uuid.uuid4()), raw=multipart,
            headers={"Content-Type": "multipart/form-data; boundary=" + boundary})
        file_id = files[0]["id"]
        assert files[0]["name"] == "Projet test/notes.txt"
        assert call("GET", prefix + "/files/" + file_id) == content
        revision = call("GET", prefix + "/settings")["conversation"]["revision"]
        call("PATCH", prefix + "/files/" + file_id, {"revision": revision, "mode": "pinned"})
        preview = call("POST", prefix + "/context", {"content": "Question suivante"})
        assert preview["context"]["filesIncluded"] == 1
        assert "DOCUMENT_DE_RECETTE" in preview["context"]["messages"][-1]["content"]
        call("PATCH", prefix + "/files/" + file_id, {"revision": revision + 1, "mode": "excluded"})
        assert call("POST", prefix + "/context", {"content": "Question suivante"})["context"]["filesIncluded"] == 0
        checks.append("file_upload_download_and_context_selection")

        archive = call("GET", prefix + "/export?format=json")
        restored = call("POST", "/import", archive, expected=201)
        conversations.append(restored["id"])
        assert restored["id"] != cid and restored["messageCount"] == 2
        assert call("GET", "/conversations/" + restored["id"] + "/export?format=json")["messages"] == archive["messages"]
        call("POST", "/import", dict(archive, schemaVersion=999), expected=400)
        assert b"## Instruction" in call("GET", prefix + "/export?format=markdown")
        search = call("GET", "/search?q=" + quote(title.upper()) + "&offset=0")
        assert any(item["id"] == cid for item in search["items"])
        latest = call("GET", prefix + "/settings")["conversation"]
        copy = call("POST", prefix + "/fork", {"revision": latest["revision"]}, expected=201)
        conversations.append(copy["id"])
        assert copy["id"] != cid and copy["messageCount"] == 2
        copied_files = call("GET", "/conversations/" + copy["id"] + "/files")
        assert len(copied_files) == 1 and copied_files[0]["id"] != file_id
        assert call("GET", "/conversations/" + copy["id"] + "/files/" + copied_files[0]["id"]) == content
        checks.append("archive_roundtrip_search_and_fork")
        if extended:
            verify_recursive(call, done, model, prefix, archive, conversations, checks)
        if tools:
            verify_tools(call, model, conversations, checks)
        return checks
    finally:
        failures = []
        for cid in conversations:
            try:
                call("DELETE", "/conversations/" + cid, expected=204)
            except Exception:
                failures.append("conversation " + cid)
        for pid in prompts:
            try:
                call("DELETE", "/prompts/" + pid, expected=204)
            except Exception:
                failures.append("profil " + pid)
        if failures:
            raise RuntimeError("Nettoyage de recette incomplet : " + ", ".join(failures))


def verify_recursive(call, done, model, prefix, archive, conversations, checks):
    """Petites sources synthétiques ; les résumés utilisent réellement le modèle choisi."""
    def revision(path):
        return call("GET", path + "/settings")["conversation"]["revision"]

    nested = io.BytesIO()
    source = b"def addition(a, b):\n    return a + b\n"
    with zipfile.ZipFile(nested, "w") as zipped:
        zipped.writestr(zipfile.ZipInfo("./src/./addition.py", (2026, 11, 1, 12, 30, 0)), source)
    outer = io.BytesIO()
    with zipfile.ZipFile(outer, "w") as zipped:
        zipped.writestr(".\\sources\\", b"")
        zipped.writestr("nested.zip", nested.getvalue())
        zipped.writestr("README.md", "# Recette\nUne fonction d’addition locale.")
        zipped.writestr(".env", "SYNTHETIC_SECRET=NE_JAMAIS_RESUMER_CE_CONTENU")
    boundary = "alycia-" + uuid.uuid4().hex
    batch = str(uuid.uuid4())
    metadata = [{"name": "recette.zip", "createdAt": "2026-11-01T09:00:00Z", "modifiedAt": "2026-11-02T09:00:00Z"}]
    multipart = (f'--{boundary}\r\nContent-Disposition: form-data; name="metadata"\r\n\r\n'
        + json.dumps(metadata) + f'\r\n--{boundary}\r\nContent-Disposition: form-data; name="files"; filename="recette.zip"\r\n'
        + 'Content-Type: application/zip\r\n\r\n').encode() + outer.getvalue() + f"\r\n--{boundary}--\r\n".encode()
    files = call("POST", prefix + "/files/" + batch, raw=multipart,
        headers={"Content-Type": "multipart/form-data; boundary=" + boundary})
    assert len(files) == 5
    root = next(f for f in files if f["name"] == "recette.zip")
    leaf = next(f for f in files if f["name"].endswith("src/addition.py"))
    assert root["sourceCreatedAt"].startswith("2026-11-01T09:00:00")
    assert leaf["sha256"].lower() == hashlib.sha256(source).hexdigest()
    assert not leaf["sourceCreatedAt"] and leaf["sourceModifiedAt"].startswith("2026-11-01T12:30:00")
    inventory = call("GET", prefix + "/inventory?batchId=" + batch).decode()
    assert all(f["name"] in inventory for f in files)
    events = call("POST", prefix + "/analyses", {"revision": revision(prefix), "batchId": batch, "budget": 1024})
    analysis = done(events)
    assert analysis["fileCount"] == 5 and analysis["summaryCalls"] >= 4
    assert any(event.get("status") for event in events)
    analysis_path = prefix + "/analyses/" + analysis["id"]
    manifest = call("GET", analysis_path + "?format=manifest")
    assert len(manifest["files"]) == 5
    excluded = next(f for f in manifest["fileSummaries"] if f["path"].endswith("/.env"))
    assert excluded["excluded"] and not excluded["summarized"]
    assert call("GET", analysis_path + "?format=tree").decode() == inventory
    summary = call("GET", analysis_path + "?format=markdown")
    assert 0 < len(summary) <= 1024
    preview = call("POST", prefix + "/context", {"content": "Explique le projet."})["context"]
    assert preview["filesIncluded"] == 1
    assert summary.decode() in preview["messages"][-1]["content"]
    call("PATCH", prefix + "/files/" + leaf["id"], {"revision": revision(prefix), "mode": "excluded"})
    report = next(a for a in call("GET", prefix + "/analyses") if a["id"] == analysis["id"])
    assert report["isStale"]
    call("PATCH", analysis_path, {"revision": revision(prefix), "useInContext": True}, expected=400)
    call("DELETE", prefix + "/files/" + root["id"], expected=204)
    assert all(f["batchId"] != batch for f in call("GET", prefix + "/files"))
    call("DELETE", analysis_path + "?revision=" + str(revision(prefix)), expected=204)
    checks.append("recursive_archives_inventory_analysis_staleness_and_delete")

    original = [{"role": "user" if i % 2 == 0 else "assistant",
        "content": f"Message {i + 1}. " + "Le projet conserve ses décisions, contraintes et tests locaux. " * 4,
        "createdAtUtc": "2026-11-16T12:00:00+00:00"} for i in range(40)]
    imported = call("POST", "/import", dict(archive, title="Recette mémoire " + uuid.uuid4().hex[:8],
        model=model, systemPrompt="SYSTEME_PROTEGE : réponds brièvement en français.", contextNotes="NOTES_PROTEGEES",
        options=dict(archive["options"], autoSummarize=True, generateUntilStop=True, structuredMemory=False), messages=original), expected=201)
    conversations.append(imported["id"])
    path = "/conversations/" + imported["id"]
    question = "DEMANDE_PROTEGEE : quelles décisions retenir ?"
    before = call("POST", path + "/context", {"content": question})["context"]
    assert before["needsSummary"] and before["summaryCalls"] == 0
    prepared = done(call("POST", path + "/context/prepare", {"revision": revision(path), "content": question}))
    context = prepared["context"]
    assert not context["needsSummary"] and context["summaryThroughSequence"] > 0 and context["summaryCalls"] > 0
    assert context["estimatedInputTokens"] <= prepared["options"]["contextTokens"] - prepared["options"]["maxOutputTokens"] - prepared["options"]["safetyTokens"]
    assert "SYSTEME_PROTEGE" in context["messages"][0]["content"] and "NOTES_PROTEGEES" in context["messages"][0]["content"]
    assert [m["content"] for m in context["messages"][-5:-1]] == [m["content"] for m in original[-4:]]
    assert context["messages"][-1]["content"] == question
    persisted = call("GET", path + "/export?format=json")["messages"]
    assert [(m["role"], m["content"]) for m in persisted] == [(m["role"], m["content"]) for m in original]
    cached = done(call("POST", path + "/context/prepare", {"revision": revision(path), "content": question}))
    assert cached["context"]["summaryCalls"] == 0
    checks.append("recursive_memory_budget_protected_messages_and_cache")
    done(call("POST", path + "/stream", {"revision": revision(path), "requestId": str(uuid.uuid4()), "content": question}))
    settings = call("GET", path + "/settings")["conversation"]
    assert settings["messageCount"] == 42 and settings["memoryText"] and settings["memorySourceHash"]
    reset = call("POST", path + "/memory/reset", {"revision": settings["revision"]})
    assert not reset["memoryText"] and reset["memoryThroughSequence"] == 0 and reset["messageCount"] == 42
    checks.append("memory_turn_persistence_and_nondestructive_reset")


def verify_tools(call, model, conversations, checks):
    conversation = call("POST", "/conversations", {"title": "Recette outils " + uuid.uuid4().hex[:8],
        "model": model, "systemPrompt": "Lis les sources avec les outils avant de répondre brièvement."}, expected=201)
    conversations.append(conversation["id"])
    prefix = "/conversations/" + conversation["id"]
    options = call("GET", prefix + "/settings")["options"]
    options.update(contextTokens=8192, maxOutputTokens=1024, useProjectTools=True, toolMaxCalls=12, temperature=0)
    call("PUT", prefix + "/settings", {"revision": conversation["revision"], "model": model,
        "systemPrompt": conversation["systemPrompt"], "contextNotes": "", "options": options})
    assert call("GET", prefix + "/tools")["modelSupportsTools"], "Le modèle n’annonce pas tools dans /api/show."
    source = ("// ligne de code synthétique\n" * 10000 + "public class CODE_SENTINEL_END { public int Value => 42; }\n").encode()
    assert len(source) > 200000
    memory = io.BytesIO()
    with zipfile.ZipFile(memory, "w") as archive:
        archive.writestr("./src/Deep.cs", source)
        archive.writestr(".env", "NE_JAMAIS_RESUMER_CE_CONTENU")
    boundary = "alycia-" + uuid.uuid4().hex
    body = (f'--{boundary}\r\nContent-Disposition: form-data; name="files"; filename="project.zip"\r\n'
        'Content-Type: application/zip\r\n\r\n').encode() + memory.getvalue() + f'\r\n--{boundary}--\r\n'.encode()
    files = call("POST", prefix + "/files/" + str(uuid.uuid4()), raw=body,
        headers={"Content-Type": "multipart/form-data; boundary=" + boundary})
    code = next(f for f in files if f["name"].endswith("Deep.cs"))
    excluded = next(f for f in files if f["name"].endswith(".env"))
    assert code["contextMode"] == "project"
    headers = {"Accept": "application/json, text/event-stream", "MCP-Protocol-Version": "2025-11-25"}

    def rpc(method, params=None, **extra):
        return call("POST", prefix + "/mcp", {"jsonrpc": "2.0", "id": 1, "method": method, "params": params or {}}, headers=headers, **extra)

    result = rpc("initialize", {"protocolVersion": "2025-11-25", "capabilities": {}, "clientInfo": {"name": "alycia-recipe", "version": "1"}})
    assert result["result"]["protocolVersion"] == "2025-11-25"
    call("POST", prefix + "/mcp", {"jsonrpc": "2.0", "method": "notifications/initialized"}, headers=headers, expected=202)
    catalog = rpc("tools/list")["result"]["tools"]
    assert {t["name"] for t in catalog} == {"project_list", "project_read", "project_search"}
    assert all(t["annotations"]["readOnlyHint"] for t in catalog)
    call("GET", prefix + "/mcp", expected=405)
    call("POST", prefix + "/mcp", {"jsonrpc": "2.0", "id": 1, "method": "ping"}, headers=dict(headers, Origin="https://example.invalid"), expected=403)
    call("POST", prefix + "/mcp", {"jsonrpc": "2.0", "id": 1, "method": "ping"},
        headers=dict(headers, **{"MCP-Protocol-Version": "2099-01-01"}), expected=400)
    assert rpc("unknown")["error"]["code"] == -32601
    assert rpc("tools/call", {"name": "run_shell", "arguments": {}})["error"]["code"] == -32602
    assert rpc("tools/call", {"name": "project_read", "arguments": {"fileId": excluded["id"]}})["result"]["isError"]
    assert rpc("tools/call", {"name": "project_read", "arguments": {"fileId": "../../etc/passwd"}})["result"]["isError"]
    offset, ids = 0, []
    while offset is not None:
        result = rpc("tools/call", {"name": "project_list", "arguments": {"offset": offset, "limit": 1}})["result"]
        page = json.loads(result["content"][0]["text"])
        ids.extend(f["fileId"] for f in page["files"])
        assert page["nextOffset"] is None or page["nextOffset"] > offset
        offset = page["nextOffset"]
    assert set(ids) == {f["id"] for f in files}
    checks.append("mcp_handshake_catalog_pagination_and_access_controls")
    result = rpc("tools/call", {"name": "project_read", "arguments": {"fileId": code["id"], "offset": len(source.decode()) - 60}})["result"]
    assert not result["isError"] and "CODE_SENTINEL_END" in result["content"][0]["text"]
    checks.append("original_read_beyond_import_excerpt")
    revision = call("GET", prefix + "/settings")["conversation"]["revision"]
    request = {"requestId": str(uuid.uuid4()), "revision": revision,
        "content": "Utilise project_search pour chercher CODE_SENTINEL_END, puis project_read pour lire le code correspondant. Explique sa valeur."}
    events = call("POST", prefix + "/stream", request)
    assert events[-1]["type"] == "done", "L’agent n’a pas terminé : " + str(events[-1].get("message", "fin absente"))
    reply = events[-1]["result"]
    trace = json.loads(reply["messages"][-1]["toolTraceJson"])
    assert any(t["name"] == "project_read" and t["succeeded"] for t in trace), "Le modèle n’a pas réellement lu le code."
    assert any(event.get("tool") for event in events)
    assert "NE_JAMAIS_RESUMER_CE_CONTENU" not in reply["messages"][-1]["content"]
    assert call("POST", prefix + "/stream", request)[-1]["result"]["replayed"]
    exported = call("GET", prefix + "/export?format=json")
    assert json.loads(exported["messages"][-1]["toolTraceJson"]) == trace
    diagnostic = json.loads(reply["messages"][-1].get("diagnosticsJson", "{}"))
    if diagnostic.get("citations"):
        citations = diagnostic["citations"]
        assert citations["supplied"], "Aucune provenance pour les lectures réussies"
        assert json.loads(exported["messages"][-1]["diagnosticsJson"]) == diagnostic
        assert not citations["unknown"], "Références inexistantes émises par le modèle"
        if "[S" in reply["messages"][-1]["content"]:
            assert citations["recognized"], "Référence émise mais non reconnue"
    checks.append("native_tool_loop_trace_persistence_export_and_replay")
    current = call("GET", prefix + "/settings")["conversation"]
    options["useProjectTools"] = False
    call("PUT", prefix + "/settings", {"revision": current["revision"], "model": model,
        "systemPrompt": conversation["systemPrompt"], "contextNotes": "", "options": options})
    assert rpc("tools/call", {"name": "project_read", "arguments": {"fileId": code["id"]}})["result"]["isError"]
    checks.append("tool_access_revocation")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://127.0.0.1:5187")
    parser.add_argument("--model", required=True, help="Nom exact d’un modèle local déjà installé")
    parser.add_argument("--extended", action="store_true", help="Tester aussi mémoire et dossiers : plusieurs générations locales supplémentaires")
    parser.add_argument("--tools", action="store_true", help="Tester MCP et les outils natifs : modèle compatible tools requis")
    args = parser.parse_args()
    result = verify(args.url, args.model, extended=args.extended, tools=args.tools)
    print(json.dumps({"passed": len(result), "checks": result, "test_data_cleaned": True}, indent=2))

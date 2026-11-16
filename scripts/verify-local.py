#!/usr/bin/env python3
"""Recette HTTP réelle. Crée puis supprime uniquement ses propres données de test."""
import argparse
import http.client
import json
import os
import uuid
from urllib.parse import quote, urlsplit


def verify(base_url, model):
    origin = urlsplit(base_url)
    if origin.scheme not in ("http", "https") or origin.hostname not in ("localhost", "127.0.0.1", "::1"):
        raise ValueError("Le backend doit être local.")
    if origin.username or origin.password or origin.query or origin.fragment or origin.path not in ("", "/"):
        raise ValueError("Origine locale invalide.")
    conversations, prompts, checks = [], [], []
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
        options.update(contextTokens=4096, maxOutputTokens=512, temperature=0.1, maxHistoryTurns=8)
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
        multipart = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"files\"; filename=\"notes.txt\"\r\n"
            "Content-Type: text/plain\r\n\r\n").encode() + content + f"\r\n--{boundary}--\r\n".encode()
        files = call("POST", prefix + "/files/" + str(uuid.uuid4()), raw=multipart,
            headers={"Content-Type": "multipart/form-data; boundary=" + boundary})
        file_id = files[0]["id"]
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


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://127.0.0.1:5187")
    parser.add_argument("--model", required=True, help="Nom exact d’un modèle local déjà installé")
    args = parser.parse_args()
    result = verify(args.url, args.model)
    print(json.dumps({"passed": len(result), "checks": result, "test_data_cleaned": True}, indent=2))

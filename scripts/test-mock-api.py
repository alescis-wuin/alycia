#!/usr/bin/env python3
"""Probe the opt-in API with no database and no model server."""
from contextlib import closing
import http.client
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
DLL = ROOT / "src/Alycia.Api/bin/Debug/net10.0/Alycia.Api.dll"


def request(port, path, headers=None):
    with closing(http.client.HTTPConnection("127.0.0.1", port, timeout=2)) as client:
        client.request("GET", path, headers=headers or {})
        response = client.getresponse()
        return response.status, response.read()


def check():
    with tempfile.TemporaryDirectory() as workspace, socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
        probe.close()
        url = f"http://127.0.0.1:{port}"
        command = ["dotnet", str(DLL), "--urls", url]
        env = {key: value for key, value in os.environ.items() if not key.startswith(("ALYCIA_", "ASPNETCORE_", "DOTNET_ENVIRONMENT"))}
        env.update(ALYCIA_MOCK_INFERENCE="true", ASPNETCORE_ENVIRONMENT="Development")
        with tempfile.TemporaryFile() as log:
            process = subprocess.Popen(command, env=env, cwd=workspace, stdout=log, stderr=log)
            try:
                for _ in range(100):
                    if process.poll() is not None:
                        raise RuntimeError("Development mock failed to start")
                    try:
                        status, data = request(port, "/api/chat/models")
                        break
                    except ConnectionRefusedError:
                        time.sleep(.1)
                else:
                    raise TimeoutError("mock startup")
                assert status == 200 and json.loads(data) == ["alycia-mock"]
                status, data = request(port, "/api/chat/mock/sample")
                assert status == 200 and json.loads(data) == {"model": "alycia-mock", "chunks": ["Bon", "jour"], "response": "Bonjour"}
                assert request(port, "/api/chat/mock/sample", {"Origin": "http://localhost"})[0] == 403
                assert request(port, "/api/chat/models", {"Host": "untrusted.invalid"})[0] == 403
            finally:
                process.terminate()
                process.wait(timeout=10)
        env["ASPNETCORE_ENVIRONMENT"] = "Production"
        production = subprocess.run(command, env=env, cwd=workspace, stdout=subprocess.PIPE,
                                    stderr=subprocess.STDOUT, timeout=10)
        assert production.returncode and b"ALYCIA_MOCK_INFERENCE requires Development" in production.stdout
        print("PASS mock inventory, ordered deltas, origin denial, host denial and Production refusal")


if __name__ == "__main__":
    check()

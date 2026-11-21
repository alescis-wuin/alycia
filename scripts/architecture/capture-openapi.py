#!/usr/bin/env python3
"""Capture the Development OpenAPI bytes under the same host and opt-ins as D00."""
import argparse
from contextlib import closing
import http.client
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]


def capture(output):
    connection = os.environ.get("ALYCIA_CHAT_TEST_CONNECTION")
    if not connection:
        raise SystemExit("ALYCIA_CHAT_TEST_CONNECTION requis pour la recette")
    dll = ROOT / "src/Alycia.Api/bin/Debug/net10.0/Alycia.Api.dll"
    with socket.socket() as address:
        address.bind(("127.0.0.1", 0))
        port = address.getsockname()[1]
    env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development", DOTNET_ENVIRONMENT="Development",
               ALYCIA_ENABLE_LOCAL_CHAT="true", ALYCIA_ENABLE_LOCAL_WORKSPACES="true", ALYCIA_MOCK_INFERENCE="false",
               ALYCIA_CHAT_CONNECTION=connection, ALYCIA_WORKSPACES_CONNECTION=connection)
    with tempfile.TemporaryDirectory() as cwd, tempfile.TemporaryFile() as log:
        process = subprocess.Popen(["dotnet", str(dll), "--urls", f"http://127.0.0.1:{port}"],
                                   cwd=cwd, env=env, stdout=log, stderr=log)
        try:
            for _ in range(100):
                if process.poll() is not None:
                    raise RuntimeError("API startup failed")
                try:
                    with closing(http.client.HTTPConnection("127.0.0.1", port, timeout=5)) as client:
                        client.request("GET", "/openapi/v1.json", headers={"Host": "localhost:8080"})
                        response = client.getresponse()
                        if response.status != 200:
                            raise RuntimeError(f"OpenAPI returned HTTP {response.status}")
                        output.write_bytes(response.read())
                        return
                except ConnectionRefusedError:
                    time.sleep(.1)
            raise TimeoutError("OpenAPI startup")
        finally:
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=10)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    capture(parser.parse_args().output)

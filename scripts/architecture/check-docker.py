#!/usr/bin/env python3
"""Build and start the actual Dockerfile and Compose services in a unique disposable project."""
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import time
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[2]
name = "alycia-accept-" + uuid.uuid4().hex
env = dict(os.environ, POSTGRES_PASSWORD=secrets.token_urlsafe(32))
command = ["docker", "compose", "-p", name, "-f", str(ROOT / "compose.yaml")]

with tempfile.TemporaryDirectory() as tmp:
    override = Path(tmp) / "ports.yaml"
    # A remote, isolated engine may forward a reserved loopback port to this runner.
    requested = os.environ.get("ALYCIA_DOCKER_TEST_PORT", "")
    if requested and (not requested.isdecimal() or not 1 <= int(requested) <= 65535):
        raise ValueError("ALYCIA_DOCKER_TEST_PORT doit être un port TCP valide")
    override.write_text(f'services:\n  api:\n    ports: !override\n      - "127.0.0.1:{requested}:8080"\n')
    command.extend(["-f", str(override)])
    subprocess.run(["docker", "info", "--format", "{{.ServerVersion}}"], check=True, env=env)
    subprocess.run(command + ["config", "--quiet"], check=True, env=env)
    try:
        subprocess.run(command + ["up", "--build", "--detach", "--wait", "--wait-timeout", "180"], cwd=ROOT, env=env, check=True)
        port = subprocess.check_output(command + ["port", "api", "8080"], env=env, text=True).strip()
        for attempt in range(40):
            try:
                with urllib.request.build_opener(urllib.request.ProxyHandler({})).open("http://" + port + "/healthz", timeout=4) as response:
                    if response.status != 200:
                        raise RuntimeError(f"Unexpected /healthz status {response.status}")
                    print("PASS Docker build, PostgreSQL dependency, API /healthz HTTP 200")
                    break
            except OSError:
                if attempt == 39:
                    raise
                time.sleep(.5)
    finally:
        subprocess.run(command + ["down", "--volumes"], env=env, check=True)

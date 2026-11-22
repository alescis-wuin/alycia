#!/usr/bin/env python3
"""Full local acceptance of the master on a disposable PostgreSQL server."""
from contextlib import nullcontext
import os
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

from test_identity import reconcile

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(os.environ.get("ALYCIA_ACCEPTANCE_OUTPUT", ROOT / "artifacts/acceptance")).resolve()
OUT.mkdir(parents=True, exist_ok=True)
if not os.getenv("ALYCIA_CHAT_TEST_CONNECTION") or not os.getenv("ALYCIA_RERANK_DIRECTORY"):
    sys.exit("ALYCIA_CHAT_TEST_CONNECTION et ALYCIA_RERANK_DIRECTORY sont requis pour la recette complète.")
env = dict(os.environ, ALYCIA_TEST_PGVECTOR="1")


def run(*args, log=None):
    cmd = list(map(str, args))
    print(subprocess.list2cmdline(cmd), flush=True)
    with (OUT / log).open("w") if log else nullcontext() as stream:
        subprocess.run(cmd, cwd=ROOT, env=env, stdout=stream, check=True)


def python(script, *args, **kw):
    run(sys.executable, ROOT / "scripts" / script, *args, **kw)


run(sys.executable, ROOT / "modules/retrieval/scripts/verify-weights.py")
python("modules.py", "bootstrap-packages")
run("dotnet", "tool", "restore")
run("dotnet", "restore", "Alycia.slnx", "--locked-mode", "--configfile", "NuGet.Config", "-m:1")
for configuration in ("Debug", "Release"):
    run("dotnet", "build", "Alycia.slnx", "--no-restore", "-c", configuration, "-m:1", "-v:q", log="build-"+configuration+".log")
for project_file in json.loads((ROOT / "modules.lock.json").read_text())["productionEntryPoints"]:
    project = Path(project_file).stem
    run("dotnet", "publish", project_file, "-c", "Release", "--no-build", "--no-restore", "-m:1", "-o", OUT/project,
        log="publish-"+project+".log")
python("test-mock-api.py", "--published", OUT/"Alycia.Api", log="mock-production.log")
run(sys.executable, "-m", "unittest", "discover", "-s", "scripts/tests", "-p", "*_tests.py")
python("architecture/check-schema.py", OUT)
with tempfile.TemporaryDirectory(prefix="run-", dir=OUT) as directory:
    current = Path(directory)
    trx = current / "trx"
    capture = current / "capture"
    python("check-modules.py", "--all-projects", "--capture", capture)
    run("dotnet", "test", "Alycia.slnx", "--no-build", "--no-restore", "-m:1", "--logger", "trx", "--results-directory", trx)
    for sub in ("trx", "capture"):
        shutil.make_archive(str(OUT/sub), "zip", current/sub)
    print(reconcile(trx, OUT/"TESTS.tsv"), "passed")
    python("architecture/finition-audit.py", capture, trx, OUT, "--require-complete", "--schema", OUT/"schema.tsv")
    for case in ("all", "originals", "mock"):
        python("tests/http-integration.py", "--case", case, log="http-"+case+".json")
    python("architecture/capture-openapi.py", OUT/"openapi.raw")
    python("evaluate-context.py", "offline", "--pipeline-v2", "--output", OUT/"rag.json")
    python("architecture/verify-baseline.py", "--http", OUT/"http-all.json", "--rag", OUT/"rag.json", "--openapi", OUT/"openapi.raw", "--trx", trx, log="compatibility.json")
python("architecture/check-docker.py", log="docker.log")
print("PASS check-master", flush=True)

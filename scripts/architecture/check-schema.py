#!/usr/bin/env python3
"""Apply both EF histories on a unique new database and compare its live catalogue."""
import argparse
import hashlib
import os
import re
from pathlib import Path
import subprocess
import uuid

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / "docs/architecture/decouplage/cloture/recette"
CONTEXTS = (("Workspaces", "modules/workspaces/src/Alycia.Modules.Workspaces.Infrastructure"),
            ("Chat", "src/Alycia.Persistence.Migrations"))


def check(out):
    # libpq variables select the same disposable server as the .NET test administrator.
    original = os.environ["ALYCIA_CHAT_TEST_CONNECTION"]
    database = "alycia_schema_" + uuid.uuid4().hex
    connection = original.rstrip(";") + ";Database=" + database
    env = dict(os.environ, ALYCIA_CHAT_CONNECTION=connection, ALYCIA_WORKSPACES_CONNECTION=connection)
    out.mkdir(parents=True, exist_ok=True)
    def ef(title, project, *args):
        return subprocess.check_output(["dotnet", "tool", "run", "dotnet-ef", *args, "--project", project,
                                       "--context", title+"DbContext", "--no-build"], env=env, cwd=ROOT)
    # CREATE fails on a collision. Cleanup is armed only after this call succeeds.
    subprocess.run(["createdb", database], env=env, check=True)
    try:
        for title, project in CONTEXTS:
            (out / (title+"-model.log")).write_bytes(ef(title, project, "migrations", "has-pending-model-changes"))
            ef(title, project, "database", "update")
            result = subprocess.check_output(["psql", "-XAt", "-v", "ON_ERROR_STOP=1", "-d", database, "-c",
                f'SELECT "MigrationId" FROM {title.lower()}."__EFMigrationsHistory" ORDER BY 1'], env=env)
            expected = re.findall(r"Applying migration '([^']+)'\.", (BASE / (title+"-migrations.log")).read_text())
            if not expected:
                raise ValueError("Reference history is empty: " + title)
            if result.decode().splitlines() != expected:
                raise ValueError(title + " historical migration IDs differ")
            (out / (title+"-history.log")).write_bytes(result)
        sql = ROOT / "scripts/architecture/finition-schema.sql"
        catalogue = subprocess.check_output(["psql", "-Xq", "-v", "ON_ERROR_STOP=1", "-d", database, "-f", str(sql)], env=env)
        (out / "schema.tsv").write_bytes(catalogue)
        if catalogue.splitlines() != (BASE / "schema.tsv").read_bytes().splitlines():
            raise ValueError("PostgreSQL catalogue differs from signed reference")
        print("PASS both EF models, both histories, catalogue SHA-256", hashlib.sha256(catalogue).hexdigest())
    finally:
        subprocess.run(["dropdb", database], env=env, check=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    check(parser.parse_args().output)

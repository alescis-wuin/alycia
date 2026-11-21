#!/usr/bin/env python3
"""Sauvegarde Compose et restauration dans un projet neuf, sans écraser les volumes existants."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import subprocess
import tarfile
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def run(args, **kwargs):
    return subprocess.run(args, cwd=ROOT, check=True, **kwargs)


def compose(windows=False, project=None):
    args = ["docker", "compose", "-f", "compose.yaml", "-f", "compose.windows.yaml" if windows else "compose.demo.yaml"]
    if project:
        args += ["--project-name", project]
    return args


def verify(directory):
    directory = Path(directory).resolve()
    manifest = json.loads((directory / "manifest.json").read_text())
    if manifest.get("schemaVersion") != 1 or set(manifest.get("files", {})) != {"database.dump", "files.tar.gz"}:
        raise ValueError("Manifest de sauvegarde invalide.")
    for name, metadata in manifest["files"].items():
        path = directory / name
        if path.stat().st_size != metadata["bytes"] or digest(path) != metadata["sha256"]:
            raise ValueError("Sauvegarde altérée : " + name)
    with tarfile.open(directory / "files.tar.gz", "r:gz") as archive:
        for member in archive:
            path = PurePosixPath(member.name)
            if path.is_absolute() or ".." in path.parts or not (member.isdir() or member.isfile()):
                raise ValueError("Chemin ou type interdit dans l’archive de fichiers.")
    return directory


def backup(args):
    os.umask(0o077)
    parent = Path(args.output).resolve()
    parent.mkdir(parents=True, exist_ok=True)
    directory = Path(tempfile.mkdtemp(prefix="alycia-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-"), dir=parent))
    command = compose(args.windows)
    running = run(command + ["ps", "--status", "running", "--services"], capture_output=True, text=True).stdout.splitlines()
    if "postgres" not in running:
        raise RuntimeError("PostgreSQL doit être démarré.")
    stopped = "chat-api" in running
    try:
        if stopped:
            run(command + ["stop", "chat-api"])
        with (directory / "database.dump").open("xb") as output:
            run(command + ["exec", "-T", "postgres", "sh", "-c",
                'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc'], stdout=output)
        with (directory / "files.tar.gz").open("xb") as output:
            run(command + ["run", "--rm", "--no-deps", "-T", "--entrypoint", "sh", "chat-api", "-c",
                "exec tar -C /app/files -czf - ."], stdout=output)
        manifest = {"schemaVersion": 1, "createdAtUtc": datetime.now(timezone.utc).isoformat(),
            "commit": run(["git", "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip(),
            "files": {name: {"bytes": (directory / name).stat().st_size, "sha256": digest(directory / name)}
                for name in ("database.dump", "files.tar.gz")}}
        (directory / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
        verify(directory)
        print("Sauvegarde vérifiée : " + str(directory))
    finally:
        if stopped:
            run(command + ["start", "chat-api"])


def restore(args):
    directory = verify(args.source)
    if not args.project.startswith("alycia-restore-") or not all(c.islower() or c.isdigit() or c == "-" for c in args.project):
        raise ValueError("Utilisez un nom neuf commençant par alycia-restore- (minuscules, chiffres et tirets).")
    # Reject even stopped containers or detached volumes from an earlier project.
    for kind in ("container", "volume"):
        existing = run(["docker", kind, "ls", "-q"] + (["-a"] if kind == "container" else [])
            + ["--filter", "label=com.docker.compose.project=" + args.project], capture_output=True, text=True).stdout.strip()
        if existing:
            raise RuntimeError("Projet de restauration déjà présent ; choisissez un autre nom.")
    command = compose(args.windows, args.project)
    env = dict(os.environ, ALYCIA_DEMO_POSTGRES_PORT="0")  # Random free loopback port on Linux.
    run(command + ["up", "-d", "--wait", "postgres"], env=env)
    with (directory / "database.dump").open("rb") as source:
        run(command + ["exec", "-T", "postgres", "sh", "-c",
            'exec pg_restore --exit-on-error --no-owner --no-acl -U "$POSTGRES_USER" -d "$POSTGRES_DB"'], stdin=source, env=env)
    with (directory / "files.tar.gz").open("rb") as source:
        run(command + ["run", "--rm", "--no-deps", "--build", "-T", "--entrypoint", "sh", "chat-api", "-c",
            "exec tar -C /app/files -xzf -"], stdin=source, env=env)
    run(command + ["exec", "-T", "postgres", "sh", "-c",
        'exec psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c '
        '\'SELECT (SELECT count(*) FROM chat.conversations) AS conversations, '
        '(SELECT count(*) FROM chat.messages) AS messages, (SELECT count(*) FROM chat.attachments) AS files;\''], env=env)
    print("Restauration isolée terminée : " + args.project + ". Aucun volume initial remplacé ; API non démarrée.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    save = sub.add_parser("create")
    save.add_argument("--output", default="backups")
    save.add_argument("--windows", action="store_true")
    check = sub.add_parser("verify")
    check.add_argument("source")
    load = sub.add_parser("restore")
    load.add_argument("source")
    load.add_argument("--project", required=True)
    load.add_argument("--windows", action="store_true")
    args = parser.parse_args()
    if args.action == "create":
        backup(args)
    elif args.action == "verify":
        print("Sauvegarde vérifiée : " + str(verify(args.source)))
    else:
        restore(args)


if __name__ == "__main__":
    main()

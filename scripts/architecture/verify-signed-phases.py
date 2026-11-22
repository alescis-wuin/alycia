#!/usr/bin/env python3
"""Verify the native signed commit sequence against the published Git trees."""
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / "docs/architecture/decouplage"


def git(*args, data=None):
    return subprocess.check_output(["git", *args], cwd=ROOT, input=data).decode().strip()


def verify():
    manifest = json.loads((DOCS / "signatures/manifest.json").read_text())
    signer = DOCS / "allowed_signers"
    prior = manifest["base"]
    items = manifest["phaseCommits"]
    if [item["phase"] for item in items] != [f"D{n:02d}" for n in range(20)]:
        raise ValueError("Incomplete phase sequence")
    for item in items:
        phase = item["phase"]
        raw = item["objectText"].encode()
        for field in ("native", "publication", "tree"):
            if not re.fullmatch(r"[0-9a-f]{40}", item[field]):
                raise ValueError(f"{phase}: invalid object name")
        # Restore only the commit metadata. The trees and blobs came from git fetch.
        header = f"commit {len(raw)}\0".encode()
        if hashlib.sha1(header + raw).hexdigest() != item["native"]:
            raise ValueError(f"{phase}: corrupt commit payload")
        native = git("hash-object", "-w", "-t", "commit", "--stdin", data=raw)
        expected = item["tree"]
        if (git("rev-parse", native + "^") != prior or git("rev-parse", native + "^{tree}") != expected
                or git("rev-parse", item["publication"] + "^{tree}") != expected):
            raise ValueError(f"{phase}: parent or tree mismatch")
        git("-c", f"gpg.ssh.allowedSignersFile={signer}", "verify-commit", native)
        print(f"{phase} {native} {expected}")
        prior = native


if __name__ == "__main__":
    verify()

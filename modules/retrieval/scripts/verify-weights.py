#!/usr/bin/env python3
"""Validate the exact weights and tokenizer used by the owner's ONNX conformance."""
import csv
import hashlib
import os
from pathlib import Path

directory = os.environ.get("ALYCIA_RERANK_DIRECTORY")
if not directory:
    raise SystemExit("ALYCIA_RERANK_DIRECTORY requis pour la conformité ONNX")
with (Path(__file__).resolve().parents[1]/"docs/onnx-sha256.tsv").open() as stream:
    for row in csv.DictReader(stream, delimiter="\t"):
        path = Path(directory) / row["fichier"]
        digest = hashlib.sha256()
        with path.open("rb") as model:
            for block in iter(lambda: model.read(1024 * 1024), b""):
                digest.update(block)
        if path.stat().st_size != int(row["octets"]) or digest.hexdigest() != row["SHA-256"]:
            raise SystemExit(f"Empreinte ONNX différente : {row['fichier']}")
        print(row["fichier"], digest.hexdigest())

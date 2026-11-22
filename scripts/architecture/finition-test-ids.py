#!/usr/bin/env python3
"""Export passing test IDs and fail if a reference case disappears or did not pass."""
import argparse
import csv
import hashlib
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

p = argparse.ArgumentParser(description=__doc__)
p.add_argument("trx", type=Path)
p.add_argument("--reference", type=Path)
a = p.parse_args()
found = set()
for f in sorted(a.trx.glob("*.trx")):
    doc = ET.parse(f)
    for result in doc.findall(".//{*}UnitTestResult"):
        # Theory display names can contain hostnames, sample credentials and paths.
        identity = result.attrib["testId"] + "\0" + result.attrib["testName"]
        row = (hashlib.sha256(identity.encode()).hexdigest(),)
        if row in found or result.get("outcome") != "Passed":
            sys.exit(f"Duplicate or unsuccessful result: {row!r}")
        found.add(row)
if not found:
    sys.exit("No VSTest results")
if a.reference:
    with a.reference.open() as stream:
        reader = csv.reader(stream, delimiter="\t")
        next(reader)
        missing = set(map(tuple, reader)) - found
    if missing:
        sys.exit(f"Missing reference cases: {sorted(missing)!r}")
writer = csv.writer(sys.stdout, delimiter="\t", lineterminator="\n")
writer.writerow(["SHA-256 (VSTest ID + NUL + testName)"])
writer.writerows(sorted(found))

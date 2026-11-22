#!/usr/bin/env python3
"""Run physical extraction checks for all modules or the historical project selectors."""
import argparse
from pathlib import Path
from modules import MANIFEST, bootstrap, extract

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("modules", nargs="*")
parser.add_argument("--build-only", action="store_true", help="compile every isolated solution when integration fixtures are unavailable")
parser.add_argument("--require-complete", action="store_true")
args = parser.parse_args()
owners = {Path(project).stem: name for name, entry in MANIFEST["modules"].items() for project in entry["projects"]}
if args.require_complete and args.modules:
    parser.error("--require-complete requires all modules and suites")
names = [owners.get(n, n) for n in args.modules]
if any(name not in MANIFEST["modules"] for name in names):
    parser.error("Sélection inconnue. Les migrations historiques sont validées par check-master.")
bootstrap()
extract(list(dict.fromkeys(names)) or sorted(MANIFEST["modules"]), "build" if args.build_only else "check")

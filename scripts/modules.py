#!/usr/bin/env python3
"""Build the local feed, test an exported module, or assemble the complete product."""
import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

from architecture.extraction import validate_layout
from architecture.layout import projects

ROOT = Path(__file__).resolve().parents[1]
FEED = ROOT / "artifacts/local-feed"
PROOF = ROOT / "docs/architecture/preparation-multidepots"
MANIFEST = json.loads((ROOT / "modules.lock.json").read_text())


def run(*args, cwd=ROOT, **kwargs):
    print(subprocess.list2cmdline([str(x) for x in args]), flush=True)
    subprocess.run(list(map(str, args)), check=True, cwd=cwd, **kwargs)


def layout():
    problems = validate_layout(ROOT)
    if problems:
        raise SystemExit("\n".join(problems))


def package_hashes():
    with (PROOF / "PACKAGES.tsv").open() as source:
        return {row["id"]: row for row in csv.DictReader(source, delimiter="\t")}


def bootstrap():
    layout()
    FEED.mkdir(parents=True, exist_ok=True)
    expected = package_hashes()
    if set(expected) != set(MANIFEST["packages"]):
        raise ValueError("Package manifest has unexpected or missing identities")
    by_name = {p.stem: p for p in projects(ROOT)}
    with tempfile.TemporaryDirectory(prefix="bootstrap-", dir=FEED.parent) as tmp:
        stage = Path(tmp)
        config = ET.parse(ROOT / "NuGet.Config")
        config.find(".//packageSources/add[@key='alycia']").set("value", str(stage))
        config.write(stage / "NuGet.Config", encoding="utf-8", xml_declaration=True)
        for name, version in sorted(MANIFEST["packages"].items(), key=lambda item: (item[0].endswith(".Mocks"), item[0])):
            project = by_name[name]
            run("dotnet", "restore", project, "--configfile", stage / "NuGet.Config", "--locked-mode", "-m:1", "-v:q")
            run("dotnet", "pack", project, "--no-restore", "-c", "Release", "-m:1", "-o", stage, "-v:q")
            package = stage / f"{name}.{version}.nupkg"
            if expected[name]["version"] != version or hashlib.sha256(package.read_bytes()).hexdigest() != expected[name]["sha256"]:
                raise ValueError(f"Local package differs from frozen {name} {version}; check SDK, source and lock")
        for package in sorted(stage.glob("*.nupkg")):
            os.replace(package, FEED / package.name)


def module_run(name, action, base, feed, *args, **kwargs):
    run(sys.executable, base / "scripts/check.py", action, "--feed", feed, *args, cwd=base, **kwargs)


def extract(names, action="check"):
    layout()
    with tempfile.TemporaryDirectory(prefix="alycia-extraction-") as temp:
        work = Path(temp).resolve()
        if work.is_relative_to(ROOT):
            raise ValueError("TMPDIR must be outside the checkout")
        feed = work / "feed"
        feed.mkdir()
        for key, row in package_hashes().items():
            name = f"{key}.{row['version']}.nupkg"
            src = FEED / name
            if hashlib.sha256(src.read_bytes()).hexdigest() != row["sha256"]:
                raise ValueError(f"bad local package: {name}")
            shutil.copyfile(src, feed / name)
        logs = ROOT / "artifacts/extraction"
        logs.mkdir(parents=True, exist_ok=True)
        # A fresh NuGet cache is shared only by this run. It contains packages and never product sources.
        env = dict(os.environ, NUGET_PACKAGES=str(work / "packages"))
        for name in names:
            with tempfile.TemporaryDirectory(prefix=name+"-", dir=work) as sub:
                base = Path(sub) / name
                source = ROOT / MANIFEST["modules"][name]["directory"]
                if any(p.is_symlink() for p in source.rglob("*") if not {"bin", "obj", "artifacts", ".git"} & set(p.relative_to(source).parts)):
                    raise ValueError(f"symlink in export {name}")
                shutil.copytree(source, base, ignore=shutil.ignore_patterns("bin", "obj", "artifacts", ".git", "TestResults", "__pycache__"))
                log = logs / (name + ".log")
                with log.open("w") as out:
                    try:
                        module_run(name, action, base, feed, "--cases-out", logs / (name + ".cases"),
                                   env=env, stdout=out, stderr=subprocess.STDOUT)
                        for project in base.glob("*/*/*.csproj"):
                            if project.stem not in MANIFEST["packages"]:
                                continue
                            packed = work / "repacked"
                            packed.mkdir(exist_ok=True)
                            run("dotnet", "pack", project, "-c", "Release", "--no-build", "--no-restore", "-m:1",
                                "-o", packed, cwd=base, env=env, stdout=out, stderr=subprocess.STDOUT)
                            filename = f"{project.stem}.{MANIFEST['packages'][project.stem]}.nupkg"
                            if (packed / filename).read_bytes() != (feed / filename).read_bytes():
                                raise ValueError(f"Independent rebuild differs from local feed: {filename}")
                            print("PASS independent package", filename, file=out)
                    except subprocess.CalledProcessError:
                        print(log.read_text()[-6000:], file=sys.stderr)
                        raise
                for assets in base.glob("*/*/obj/project.assets.json"):
                    if str(ROOT) in assets.read_text():
                        raise ValueError(f"checkout leaked into export: {assets}")
                print(next((line for line in log.read_text().splitlines() if line.startswith("PASS ")), f"PASS build {name}"), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("bootstrap-packages", "build-module", "test-module", "check-module", "check-extraction", "check-master"))
    parser.add_argument("name", nargs="?", choices=sorted(MANIFEST["modules"]))
    args = parser.parse_args()
    if not shutil.which("dotnet"):
        parser.error("SDK .NET 10 requis")
    if args.command.endswith("-module") != bool(args.name) and not (args.command == "check-extraction" and args.name):
        parser.error("Indiquer un domaine pour une commande de module")
    if args.command == "bootstrap-packages":
        bootstrap()
    elif args.command == "check-extraction":
        extract([args.name] if args.name else sorted(MANIFEST["modules"]))
    elif args.command == "check-master":
        layout()
        run(sys.executable, ROOT / "scripts/architecture/accept-master.py")
    else:
        layout()
        module_run(args.name, args.command.split("-")[0], ROOT / MANIFEST["modules"][args.name]["directory"], FEED)


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Build each protected assembly with only its permitted local project inputs."""
from pathlib import Path
import argparse
import importlib.util
import shutil
import subprocess
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
spec = importlib.util.spec_from_file_location("alycia_guard", HERE / "check-modules.py")
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


CONTRACT_SUITES = {"Alycia.Modules.Inference.Tests": {"Alycia.Contracts", "Alycia.Modules.Inference", "Alycia.Modules.Testing"}}


def build(name):
    with tempfile.TemporaryDirectory(prefix="alycia-isolated-") as tmp:
        workspace = Path(tmp)
        for file in ("global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.Config", "nuget.config"):
            source = ROOT / file
            if source.is_file():
                shutil.copy2(source, workspace / file)
        is_suite = name in CONTRACT_SUITES
        dependencies = CONTRACT_SUITES[name] if is_suite else guard.ISOLATED[name]
        for item in sorted({name} | dependencies):
            group = "tests" if item in CONTRACT_SUITES else "src"
            shutil.copytree(ROOT / group / item, workspace / group / item,
                            ignore=shutil.ignore_patterns("bin", "obj", ".vs"))
        project = Path("tests" if is_suite else "src", name, name + ".csproj")
        subprocess.run(["dotnet", "test" if is_suite else "build", str(project), "-c", "Release", "-m:1", "--nologo", "-v:q"],
                       cwd=workspace, check=True)
        print(f"PASS {name}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("modules", nargs="*", metavar="MODULE")
    selected = parser.parse_args().modules
    for item in selected or sorted(guard.ISOLATED.keys() | CONTRACT_SUITES.keys()):
        if item not in guard.ISOLATED and item not in CONTRACT_SUITES:
            parser.error(f"unregistered module {item}")
        build(item)

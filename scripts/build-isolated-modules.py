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


CONTRACT_SUITES = {
    "Alycia.Modules.Conversations.Tests": {"Alycia.Modules.Chat.Domain", "Alycia.Modules.Chat.Application",
        "Alycia.Modules.Chat.Infrastructure", "Alycia.Contracts", "Alycia.Platform.Persistence", "Alycia.Modules.Testing"},
    "Alycia.Modules.Configuration.Tests": {"Alycia.Contracts", "Alycia.Modules.Configuration"},
    "Alycia.Modules.Inference.Tests": {"Alycia.Contracts", "Alycia.Modules.Inference", "Alycia.Modules.Testing"},
    "Alycia.Modules.Workspaces.Tests": {"Alycia.Modules.Workspaces.Domain", "Alycia.Modules.Workspaces.Application",
        "Alycia.Modules.Workspaces.Infrastructure", "Alycia.Contracts", "Alycia.Platform.Persistence"},
    "Alycia.Modules.Context.Tests": {"Alycia.Contracts", "Alycia.Modules.Context", "Alycia.Modules.Testing"},
    "Alycia.Modules.Tools.Tests": {"Alycia.Contracts", "Alycia.Modules.Tools", "Alycia.Modules.Testing"},
    "Alycia.Modules.Documents.Tests": {"Alycia.Contracts", "Alycia.Modules.Documents", "Alycia.Platform.Persistence", "Alycia.Modules.Documents.Infrastructure"},
    "Alycia.Modules.Indexing.Tests": {"Alycia.Contracts", "Alycia.Modules.Indexing", "Alycia.Modules.Testing"},
    "Alycia.Modules.Retrieval.Tests": {"Alycia.Contracts", "Alycia.Modules.Retrieval", "Alycia.Modules.Retrieval.Onnx", "Alycia.Modules.Testing"},
    "Alycia.Modules.Memory.Tests": {"Alycia.Contracts", "Alycia.Modules.Memory", "Alycia.Modules.Testing"},
}


def build(name, independent_inputs=None):
    with tempfile.TemporaryDirectory(prefix="alycia-isolated-") as tmp:
        workspace = Path(tmp)
        for file in ("global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.Config", "nuget.config"):
            source = ROOT / file
            if source.is_file():
                shutil.copy2(source, workspace / file)
        is_suite = name in CONTRACT_SUITES
        dependencies = independent_inputs
        if dependencies is None:
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
    parser.add_argument("--require-complete", action="store_true", help="require autonomous builds and an owner suite for every product module")
    args = parser.parse_args()
    selected = args.modules
    for item in selected or sorted(guard.ISOLATED.keys() | CONTRACT_SUITES.keys()):
        if item not in guard.ISOLATED and item not in CONTRACT_SUITES:
            parser.error(f"unregistered module {item}")
        build(item)
    if args.require_complete:
        projects = {p.stem for p in (ROOT / "src").glob("Alycia.Modules.*/*.csproj")}
        if selected:
            parser.error("--require-complete requires all modules and suites")
        uncovered = projects - set().union(*CONTRACT_SUITES.values())
        missing = projects - guard.ISOLATED.keys()
        if missing or uncovered:
            raise SystemExit("Modules sans compilation ou suite autonome: " + ", ".join(sorted(missing | uncovered)))

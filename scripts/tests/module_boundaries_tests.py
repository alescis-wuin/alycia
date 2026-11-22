"""Exercise the architecture guard on small real C# source/project graphs."""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

GUARD = Path(__file__).resolve().parents[1] / "check-modules.py"
APP = "Alycia.Modules.Chat.Application"
INFRA = "Alycia.Modules.Chat.Infrastructure"
CLIENT = "Alycia.Client.Avalonia"
CONTRACTS = "Alycia.Contracts"


class ModuleBoundariesTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.solution = ET.Element("Solution")

    def project(self, name, references=(), group="src", packages=()):
        path = self.root / group / name / (name + ".csproj")
        path.parent.mkdir(parents=True)
        project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
        props = ET.SubElement(project, "PropertyGroup")
        ET.SubElement(props, "TargetFramework").text = "net10.0"
        items = ET.SubElement(project, "ItemGroup")
        for package, version in packages:
            ET.SubElement(items, "PackageReference", Include=package, Version=version)
        for ref in references:
            ET.SubElement(items, "ProjectReference", Include=f"../{ref}/{ref}.csproj")
        ET.ElementTree(project).write(path)
        ET.SubElement(self.solution, "Project", Path=path.relative_to(self.root).as_posix())
        return path.parent

    def source(self, project, file, code):
        path = project / file
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(code)

    def guard(self, valid):
        ET.ElementTree(self.solution).write(self.root / "Alycia.slnx")
        result = subprocess.run([sys.executable, GUARD, "--root", self.root], capture_output=True, text=True)
        self.assertEqual(valid, result.returncode == 0, result.stderr)
        return result.stderr

    def test_inference_rejects_workspace_contract_even_through_alias(self):
        contracts = self.project(CONTRACTS)
        inference = self.project("Alycia.Modules.Inference", [CONTRACTS])
        self.source(contracts, "Profile.cs", "namespace Alycia.Contracts.Workspaces; public record Profile(int Revision);")
        self.source(inference, "Generate.cs", "using P = Alycia.Contracts.Workspaces.Profile; class Generate(P value);")
        self.assertIn("contract boundary: Alycia.Modules.Inference -> T:Alycia.Contracts.Workspaces.Profile", self.guard(False))

    def test_assembly_and_logical_context_refuse_mutable_settings_and_tracked_entities(self):
        contracts = self.project(CONTRACTS)
        self.source(contracts, "ChatSettings.cs", "namespace Alycia.Contracts.Chat; public class ChatSettings {}")
        domain = self.project("Alycia.Modules.Chat.Domain")
        self.source(domain, "Tracked.cs", "namespace Alycia.Modules.Chat.Domain; public class ChatMessage {}")
        app = self.project(APP, [CONTRACTS, "Alycia.Modules.Chat.Domain"])
        self.source(app, "ChatOptions.cs", "namespace Alycia.Modules.Chat.Application; public class ChatOptions {}")
        self.source(app, "Context/Incoming.cs", """
            using Runtime = Alycia.Modules.Chat.Application.ChatOptions;
            using Tracked = Alycia.Modules.Chat.Domain.ChatMessage;
            public class Incoming { public (Runtime, Tracked) Echo(Runtime options, Tracked row) => (options, row); }
            """)
        physical = self.project("Alycia.Modules.Inference", [CONTRACTS])
        self.source(physical, "Inputs.cs", "using Wire = Alycia.Contracts.Chat.ChatSettings; public class Inputs(Wire options);")
        errors = self.guard(False)
        self.assertIn("specialized input: Context -> ChatOptions", errors)
        self.assertIn("specialized input: Context -> ChatMessage", errors)
        self.assertIn("specialized input: Inference -> ChatSettings", errors)

    def test_two_contract_compatible_edges_cannot_form_a_business_cycle(self):
        app = self.project(APP)
        self.source(app, "Context/ContextValue.cs", "public class ContextValue { public MemoryValue Read() => null; }")
        self.source(app, "Memory/MemoryValue.cs", "public class MemoryValue { public ContextValue Read() => null; }")
        self.assertIn("logical owner cycle: Context -> Memory -> Context", self.guard(False))

    def test_owner_cycle_across_a_physical_module_and_a_logical_owner_is_denied(self):
        context = self.project("Alycia.Modules.Context")
        self.source(context, "Policy.cs", "namespace Isolated; public class Policy {}")
        app = self.project(APP, ["Alycia.Modules.Context"])
        self.source(app, "Context/ContextBuilder.cs", "public class ContextBuilder { public MemoryValue Read() => null; }")
        self.source(app, "Memory/MemoryValue.cs", "public class MemoryValue { public Isolated.Policy Read() => null; }")
        self.assertIn("logical owner cycle: Context -> Memory -> Context", self.guard(False))

    def test_independent_module_cannot_reference_a_sibling_implementation(self):
        core = "Alycia.Modules.Retrieval"
        sibling = "Alycia.Modules.Indexing"
        self.project(sibling)
        self.project(core, [sibling])
        self.assertIn(f"project layer: {core} -> {sibling}", self.guard(False))

    def test_allowed_port_and_nested_namespace_ignore_generated_test_and_text(self):
        app = self.project(APP)
        infra = self.project(INFRA, [APP])
        tests = self.project("Alycia.Modules.Chat.Tests", group="tests")
        self.source(app, "Tools/Port.cs", f"namespace {APP}; public interface ITurnTools {{ }}")
        self.source(app, "Context/Use.cs", f"""
using Port = global::{APP}.ITurnTools;
namespace {APP}.Context.Nested;
internal record Use(Port Tools) {{ const string Hint = "using Npgsql;"; }}
""")
        self.source(infra, "Documents/Parser.cs", f"namespace {INFRA}; public class Parser {{ }}")
        self.source(infra, "Documents/Meta.g.cs", f"namespace {INFRA}; class Meta {{ Parser allowed; }}")
        self.source(infra, "obj/Generated.cs", "this file is excluded by MSBuild")
        self.source(tests, "Example.cs", f"using {INFRA}; using Npgsql;")
        self.guard(True)

    def test_same_assembly_and_namespace_and_concrete_port_bypass_are_denied(self):
        app = self.project(APP)
        infra = self.project(INFRA)
        self.source(infra, "Documents/Reader.cs", f"namespace {INFRA}; public class Reader {{ }}")
        self.source(infra, "Inference/Model.cs", f"namespace {INFRA}; class Model {{ Reader Read() => new(); }}")
        self.source(app, "Tools/TurnTools.cs", f"namespace {APP}; public class TurnTools {{ }}")
        self.source(app, "Context/Prepare.cs", f"using X = global::{APP}.TurnTools; namespace {APP}.Context; class Prepare(X x);")
        diagnostics = self.guard(False)
        self.assertIn("logical boundary: Inference -> Documents (Reader)", diagnostics)
        self.assertIn("narrow boundary: Context -> Tools.TurnTools", diagnostics)

    def test_service_port_and_endpoint_persistence_are_denied(self):
        app = self.project(APP)
        api = self.project("Alycia.Api")
        self.source(app, "Tools/BadPort.cs", f"namespace {APP}; interface BadPort {{ System.Net.Http.HttpClient Open(); }}")
        self.source(api, "Chat/Read.cs", "class ChatDbContext {} class Route { void Read(ChatDbContext db) {} }")
        diagnostics = self.guard(False)
        self.assertIn("business port exposes technical type System.Net.Http.HttpClient", diagnostics)
        self.assertIn("endpoint persistence belongs in the database adapter", diagnostics)

    def test_client_transitive_product_dependency_and_project_cycle_are_denied(self):
        self.project(CLIENT, [CONTRACTS])
        self.project(CONTRACTS, ["Alycia.Extra"])
        self.project("Alycia.Extra", [CONTRACTS])
        diagnostics = self.guard(False)
        self.assertIn("cycle:", diagnostics)
        self.assertIn(f"{CLIENT} transitively reaches Alycia.Extra", diagnostics)

    def test_short_exception_name_is_bounded_to_the_typed_diagnostic_projection(self):
        contracts = self.project(CONTRACTS)
        self.source(contracts, "Diagnostics/FailureType.cs", """
namespace Alycia.Contracts.Diagnostics;
public static class FailureType {
    public static string Name(System.Exception error) => error.GetType().Name;
    public static string Inspect(System.Exception error) => error.GetType().Assembly.FullName!;
}""")
        errors = self.guard(False)
        self.assertEqual(1, errors.count("unreviewed Y:"), errors)

    def test_markdown_block_name_is_bounded_to_a_markdig_block_and_its_short_name(self):
        client = self.project(CLIENT, packages=[("Markdig", "1.6.0")])
        self.source(client, "MarkdownView.cs", """
namespace Alycia.Client.Avalonia;
class MarkdownView {
    private static string BlockKind(Markdig.Syntax.Block block) => block.GetType().Name;
    private static string BlockKind(object block) => block.GetType().Name;
    private static string Inspect(Markdig.Syntax.Block block) => block.GetType().Assembly.FullName!;
}""")
        errors = self.guard(False)
        self.assertEqual(2, errors.count("unreviewed Y:"), errors)

    def test_resolved_nuget_type_sql_and_dynamic_activation(self):
        packages = [("Npgsql", "10.0.3")]
        app = self.project(APP, packages=packages)
        infra = self.project(INFRA, packages=packages)
        self.source(app, "Tools/Port.cs", "using Conn = Npgsql.NpgsqlConnection; interface Port { Conn Open(); }")
        self.source(infra, "Tools/Reader.cs", """
            using Npgsql;
            class Reader {
                NpgsqlCommand Read() => new NpgsqlCommand("SELECT id FROM workspaces.workspaces");
                object? Activate(System.Type t) => System.Activator.CreateInstance(t);
            }
            """)
        errors = self.guard(False)
        self.assertIn("technical provider Npgsql", errors)
        self.assertIn("unreviewed S: Tools -> workspaces.workspaces", errors)
        self.assertIn("unreviewed Y: Tools -> System.Activator.CreateInstance", errors)

    def test_missing_compiled_type_fails_even_under_a_preprocessor_condition(self):
        app = self.project(APP)
        self.source(app, "Tools/Unresolved.cs",
                    "#if NET10_0_OR_GREATER\nclass Unresolved { NonexistentProvider value; }\n#endif")
        self.assertIn("CS0246", self.guard(False))

    def test_handwritten_generated_suffix_cannot_hide_forbidden_type(self):
        app = self.project(APP)
        self.source(app, "Tools/Concrete.cs", f"namespace {APP}; public class Concrete {{}}")
        self.source(app, "Context/Generated.g.cs", f"// <auto-generated/>\nnamespace {APP}; class Use(Concrete value);")
        self.assertIn("narrow boundary: Context -> Tools.Concrete", self.guard(False))

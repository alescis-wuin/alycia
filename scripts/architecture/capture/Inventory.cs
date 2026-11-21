using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Alycia.Architecture;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Inventory : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Failure = new("ALY001", "Architecture inventory", "{0}",
        "Architecture", DiagnosticSeverity.Error, isEnabledByDefault: true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Failure];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(Capture);
    }

    private static void Capture(CompilationAnalysisContext analysis)
    {
        var config = analysis.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        if (!config.TryGetValue("build_property.AlyciaArchitectureOutput", out var output) ||
            !config.TryGetValue("build_property.AlyciaArchitectureRoot", out var root) ||
            !config.TryGetValue("build_property.MSBuildProjectFullPath", out var project))
        {
            analysis.ReportDiagnostic(Diagnostic.Create(Failure, Location.None, "Missing MSBuild inventory context"));
            return;
        }
        var rows = new SortedSet<string>(StringComparer.Ordinal);
        var compilation = analysis.Compilation;
        string Relative(string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        void Row(params string[] fields) => rows.Add(string.Join('\t', fields.Select(f => f.Replace('\t', ' ').ReplaceLineEndings(" "))));
        Row("A", Relative(project), compilation.AssemblyName!);
        foreach (var reference in compilation.ReferencedAssemblyNames) Row("B", Relative(project), reference.Name);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var path = Path.IsPathRooted(tree.FilePath) ? Relative(tree.FilePath) :
                Relative(Path.Combine(Path.GetDirectoryName(project)!, "obj", tree.FilePath));
            var unit = tree.GetRoot(analysis.CancellationToken);
            Row("F", path);
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in unit.DescendantNodes())
            {
                if (node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or BaseMethodDeclarationSyntax
                    or BasePropertyDeclarationSyntax or EnumMemberDeclarationSyntax or VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax })
                {
                    if (model.GetDeclaredSymbol(node, analysis.CancellationToken) is { } s)
                    {
                        var id = s.GetDocumentationCommentId() ?? s.ToDisplayString();
                        Row("D", path, Line(node), s.Kind.ToString(), s.DeclaredAccessibility.ToString(), id);
                    }
                }
                if (node is RecordDeclarationSyntax record && record.ParameterList is { } parameters &&
                    model.GetDeclaredSymbol(record, analysis.CancellationToken) is INamedTypeSymbol type)
                    foreach (var param in parameters.Parameters)
                        foreach (var prop in type.GetMembers(param.Identifier.ValueText).OfType<IPropertySymbol>())
                            Row("D", path, Line(param), "Property", prop.DeclaredAccessibility.ToString(), prop.GetDocumentationCommentId()!);
                if (node is InvocationExpressionSyntax or ObjectCreationExpressionSyntax)
                {
                    var operation = model.GetSymbolInfo(node, analysis.CancellationToken).Symbol as IMethodSymbol;
                    var name = operation?.Name;
                    if (name is not null && (name.StartsWith("ExecuteSql", StringComparison.Ordinal) ||
                        name.StartsWith("FromSql", StringComparison.Ordinal) || name.StartsWith("SqlQuery", StringComparison.Ordinal) ||
                        operation!.MethodKind == MethodKind.Constructor && operation.ContainingType.Name == "NpgsqlCommand"))
                    {
                        var argument = node is InvocationExpressionSyntax call ? call.ArgumentList.Arguments.FirstOrDefault()?.Expression
                            : ((ObjectCreationExpressionSyntax)node).ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                        if (argument is not null)
                        {
                            var literal = model.GetConstantValue(argument, analysis.CancellationToken);
                            var sql = literal.HasValue ? literal.Value as string : null;
                            if (argument is InterpolatedStringExpressionSyntax interpolation)
                            {
                                sql = string.Join(" ", interpolation.Contents.OfType<InterpolatedStringTextSyntax>().Select(t => t.TextToken.ValueText));
                                if (interpolation.Contents.OfType<InterpolationSyntax>().Any() && operation!.ContainingType.Name == "NpgsqlCommand")
                                    Row("Y", path, Line(node), "NpgsqlCommand with interpolated SQL");
                            }
                            if (sql is null) Row("Y", path, Line(node), "SQL resolved at runtime: " + name);
                            else foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(sql,
                                @"(?i)\b(chat|workspaces)\.([a-z_][a-z0-9_]*)"))
                                Row("S", path, Line(node), match.Value.ToLowerInvariant());
                        }
                    }
                }
                if (node is not (SimpleNameSyntax or ImplicitObjectCreationExpressionSyntax or AttributeSyntax or ConstructorInitializerSyntax)) continue;
                var symbol = model.GetSymbolInfo(node, analysis.CancellationToken).Symbol;
                if (symbol is IAliasSymbol alias) symbol = alias.Target;
                if (symbol is INamespaceSymbol) continue;
                var referenced = symbol switch
                {
                    INamedTypeSymbol named => named,
                    IMethodSymbol method => (method.ReducedFrom ?? method).ContainingType,
                    IPropertySymbol or IFieldSymbol or IEventSymbol => symbol.ContainingType,
                    _ => null
                };
                if (referenced?.ContainingAssembly is null || referenced.TypeKind == TypeKind.Error) continue;
                var outer = referenced;
                while (outer.ContainingType is not null) outer = outer.ContainingType;
                var caller = node.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().LastOrDefault()?.Identifier.ValueText ?? "";
                var target = outer.OriginalDefinition;
                if (target.ContainingAssembly.Name.StartsWith("Alycia.", StringComparison.Ordinal))
                    Row("R", path, Line(node), caller, target.ContainingAssembly.Name, target.GetDocumentationCommentId()!, target.Name);
                if (referenced.ContainingNamespace is { } ns)
                {
                    var name = ns.ToDisplayString();
                    if (name.StartsWith("Npgsql", StringComparison.Ordinal) || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                        || name.StartsWith("Microsoft.ML.OnnxRuntime", StringComparison.Ordinal))
                        Row("N", path, Line(node), name);
                    if ((name == "System.Net.Http" || name == "System.Data.Common") && node.Ancestors().OfType<InterfaceDeclarationSyntax>().Any())
                        Row("T", path, Line(node), referenced.ToDisplayString());
                }
                if (symbol is IMethodSymbol methodSymbol && node is SimpleNameSyntax)
                {
                    if (methodSymbol.Name is "GetType" or "CreateInstance" or "LoadFrom" or "MakeGenericType"
                        && referenced.ContainingNamespace.ToDisplayString() is "System" or "System.Reflection")
                        Row("Y", path, Line(node), methodSymbol.ToDisplayString());
                    if (methodSymbol.Name is "AddScoped" or "AddTransient" or "AddSingleton" or "AddHostedService" or "AddHttpClient" or "AddDbContext"
                        && methodSymbol.ContainingNamespace.ToDisplayString().StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal))
                        Row("L", path, Line(node), methodSymbol.Name, string.Join(", ", methodSymbol.TypeArguments.Select(a => a.ToDisplayString())));
                }
                if (symbol is IPropertySymbol property && property.ContainingType.Name.EndsWith("DbContext", StringComparison.Ordinal)
                    && property.Type is INamedTypeSymbol { Name: "DbSet" } set)
                    Row("E", path, Line(node), property.ContainingType.Name, property.Name, set.TypeArguments[0].Name);
                if (symbol?.ContainingType is { Name: "ChatSettings" or "ChatOptions" } && symbol is IPropertySymbol setting)
                    Row("C", path, Line(node), caller, setting.Name);
            }
        }
        var destination = Path.Combine(output, compilation.AssemblyName + ".tsv");
        File.WriteAllLines(destination, rows);
    }

    private static string Line(SyntaxNode node) => (node.GetLocation().GetLineSpan().StartLinePosition.Line + 1).ToString();
}

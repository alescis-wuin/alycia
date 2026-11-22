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
                        if (s is INamedTypeSymbol declaration)
                        {
                            if (declaration.TypeKind == TypeKind.Interface && declaration.DeclaredAccessibility == Accessibility.Public)
                                Row("I", path, declaration.ToDisplayString());
                            else if (declaration.TypeKind is TypeKind.Class or TypeKind.Struct && !declaration.IsAbstract)
                                foreach (var port in declaration.AllInterfaces.Where(p => p.ContainingAssembly.Name.StartsWith("Alycia.", StringComparison.Ordinal)))
                                    Row("M", path, declaration.ToDisplayString(), port.ToDisplayString());
                        }
                    }
                }
                if (node is RecordDeclarationSyntax record && record.ParameterList is { } parameters &&
                    model.GetDeclaredSymbol(record, analysis.CancellationToken) is INamedTypeSymbol type)
                    foreach (var param in parameters.Parameters)
                        foreach (var prop in type.GetMembers(param.Identifier.ValueText).OfType<IPropertySymbol>())
                            Row("D", path, Line(param), "Property", prop.DeclaredAccessibility.ToString(), prop.GetDocumentationCommentId()!);
                if (node is UsingDirectiveSyntax { Name: { } imported })
                    Row("N", path, Line(node), imported.ToString().Replace("global::", ""));
                if (node is SimpleNameSyntax endpointName && path.StartsWith("src/Alycia.Api/", StringComparison.Ordinal)
                    && path is not ("src/Alycia.Api/Chat/ChatRegistration.cs" or "src/Alycia.Api/Workspaces/WorkspaceRegistration.cs" or "src/Alycia.Api/Program.cs"))
                {
                    var token = endpointName.Identifier.ValueText;
                    if (token is "ChatDbContext" or "WorkspacesDbContext" or "DbContext" or "DbSet" or "DatabaseFacade" or "AddDbContext"
                        or "ExecuteUpdate" or "ExecuteUpdateAsync" or "ExecuteDelete" or "ExecuteDeleteAsync" ||
                        token.StartsWith("FromSql", StringComparison.Ordinal) || token.StartsWith("ExecuteSql", StringComparison.Ordinal)
                        || token.StartsWith("SqlQuery", StringComparison.Ordinal))
                        Row("P", path, Line(node), token);
                }
                if (node is AssignmentExpressionSyntax assignment &&
                    model.GetSymbolInfo(assignment.Left, analysis.CancellationToken).Symbol is IPropertySymbol { Name: "CommandText" } commandText &&
                    commandText.ContainingNamespace.ToDisplayString() is "System.Data.Common" or "Npgsql")
                {
                    if (model.GetConstantValue(assignment.Right, analysis.CancellationToken).Value is string command)
                    {
                        foreach (var relation in Relations(command)) Row("S", path, Line(node), relation);
                    }
                    else Row("Y", path, Line(node), "CommandText resolved at runtime");
                }
                if (node is InvocationExpressionSyntax or ObjectCreationExpressionSyntax)
                {
                    var operation = model.GetSymbolInfo(node, analysis.CancellationToken).Symbol as IMethodSymbol;
                    var name = operation?.Name;
                    if (operation?.ContainingType.ToDisplayString() == "Microsoft.EntityFrameworkCore.DbContext"
                        && name is ("Set" or "Find" or "FindAsync" or "Add" or "AddAsync" or "Attach" or "Update" or "Remove" or "Entry"))
                    {
                        if (operation.TypeArguments.Length == 1)
                        {
                            var entity = operation.TypeArguments[0].ToDisplayString();
                            var mapping = name == "Set" && node.Parent is ArrowExpressionClauseSyntax getter &&
                                getter.Parent is PropertyDeclarationSyntax declaration &&
                                model.GetDeclaredSymbol(declaration) is IPropertySymbol { Type: INamedTypeSymbol { Name: "DbSet" } dbSet } &&
                                SymbolEqualityComparer.Default.Equals(dbSet.TypeArguments[0], operation.TypeArguments[0]);
                            Row(mapping ? "H" : "E", path, Line(node), "DbContext", entity, entity);
                        }
                        else Row("Y", path, Line(node), "non-generic EF access: " + name);
                    }
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
                            else foreach (var relation in Relations(sql)) Row("S", path, Line(node), relation);
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
                    {
                        if (IsTypeNameBoundary(node, model))
                            Row("K", path, Line(node), "diagnostic-or-markdown-type-name");
                        else
                            Row("Y", path, Line(node), methodSymbol.ToDisplayString());
                    }
                    if ((methodSymbol.Name is "GetService" or "GetRequiredService" or "GetKeyedService" or "GetRequiredKeyedService" or "GetServices"
                        && (methodSymbol.ContainingType.ToDisplayString() == "System.IServiceProvider"
                            || methodSymbol.ContainingNamespace.ToDisplayString().StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal)))
                        || methodSymbol.ContainingType.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.ActivatorUtilities")
                        Row("L", path, Line(node), methodSymbol.Name, methodSymbol.ContainingType.ToDisplayString());
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

    // These two value projections return the short name of a statically known parameter type.
    // Loading a type, inspecting members, or returning a Type never matches this expression.
    private static bool IsTypeNameBoundary(SyntaxNode node, SemanticModel model)
    {
        if (node.Parent is not MemberAccessExpressionSyntax access || access.Name.Identifier.Text != "GetType"
            || access.Parent is not InvocationExpressionSyntax call || call.ArgumentList.Arguments.Count != 0
            || call.Parent is not MemberAccessExpressionSyntax name || name.Expression != call
            || model.GetSymbolInfo(name).Symbol is not IPropertySymbol property || property.Name != "Name"
            || property.ContainingType.ToDisplayString() is not ("System.Reflection.MemberInfo" or "System.Type")
            || name.Parent is not ArrowExpressionClauseSyntax { Parent: MethodDeclarationSyntax declaration }
            || model.GetDeclaredSymbol(declaration) is not IMethodSymbol method || method.Parameters.Length != 1
            || !method.IsStatic || method.ReturnType.SpecialType != SpecialType.System_String
            || model.GetSymbolInfo(access.Expression).Symbol is not IParameterSymbol parameter
            || !SymbolEqualityComparer.Default.Equals(parameter, method.Parameters[0])) return false;
        return (method.ContainingAssembly.Name, method.ContainingType.ToDisplayString(), method.Name) switch
        {
            ("Alycia.Contracts", "Alycia.Contracts.Diagnostics.FailureType", "Name") =>
                SymbolEqualityComparer.Default.Equals(parameter.Type, model.Compilation.GetTypeByMetadataName("System.Exception")),
            ("Alycia.Client.Avalonia", "Alycia.Client.Avalonia.MarkdownView", "BlockKind") =>
                parameter.Type.ContainingAssembly.Name == "Markdig" && parameter.Type.ToDisplayString() == "Markdig.Syntax.Block",
            _ => false
        };
    }

    private static IEnumerable<string> Relations(string sql)
    {
        const string qualified = """(?i)(?<![a-z0-9_])"?(chat|workspaces)"?\s*\.\s*"?([a-z_][a-z0-9_]*)"?""";
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(sql, qualified))
            yield return (match.Groups[1].Value + "." + match.Groups[2].Value).ToLowerInvariant();
        // A change of search_path must not bypass ownership by dropping the schema.
        const string unqualified = """(?i)\b(?:FROM|JOIN|UPDATE|INTO|TABLE)\s+"?(attachments|workspace_profiles|workspace_file_overrides|document_analyses|conversations|messages|embedding_jobs|embedding_vectors|knowledge_units|temporal_memory|prompt_profiles|import_settings|workspaces)"?(?![\w".])""";
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(sql, unqualified))
        {
            var table = match.Groups[1].Value.ToLowerInvariant();
            yield return (table == "workspaces" ? "workspaces." : "chat.") + table;
        }
    }

    private static string Line(SyntaxNode node) => (node.GetLocation().GetLineSpan().StartLinePosition.Line + 1).ToString();
}

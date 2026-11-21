namespace Alycia.Contracts.Chat;

public static class SourceFileType
{
    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".cs", ".axaml", ".fs", ".vb", ".py", ".js", ".jsx", ".ts", ".tsx", ".rs", ".go", ".java", ".kt",
        ".c", ".h", ".cpp", ".hpp", ".swift", ".php", ".rb", ".sh", ".ps1", ".sql", ".html", ".css", ".vue", ".svelte" };
    public static bool IsCode(string path) => CodeExtensions.Contains(Path.GetExtension(path));
}

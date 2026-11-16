namespace Alycia.Modules.Chat.Application;

// Convenience exclusions for project imports, not a secret detector.
public static class ImportPolicy
{
    public static bool Excluded(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/');
        var directories = new[] { ".git", ".svn", ".hg", ".venv", "venv", "node_modules", "__pycache__", "bin", "obj", ".ssh", ".aws" };
        if (parts.Any(part => directories.Contains(part, StringComparer.OrdinalIgnoreCase))) return true;
        var name = parts[^1];
        return name.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
            || name is "id_rsa" or "id_ed25519" or "credentials"
            || name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".key", StringComparison.OrdinalIgnoreCase);
    }
}

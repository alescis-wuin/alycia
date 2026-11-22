using Alycia.Contracts.Chat;
using System.Text;
using System.Text.Json;

namespace Alycia.Contracts.Tools;

public static class ToolArguments
{
    public static void Validate(string name, JsonElement arguments, int maxResultBytes)
    {
        if (maxResultBytes is < 1024 or > 131072) throw new ArgumentException("Budget de résultat invalide.");
        if (arguments.ValueKind != JsonValueKind.Object || arguments.GetRawText().Length > 4096)
            throw new ArgumentException("Arguments invalides.");
        string[] allowed = name switch
        {
            "project_list" => ["offset", "limit", "codeOnly"],
            "project_read" => ["fileId", "offset", "maxBytes", "symbol"],
            "project_search" => ["query", "offset", "limit"],
            "project_search_passages" or "project_find_symbols" or "project_find_references" => ["query", "offset", "limit"],
            "project_read_many" => ["reads"],
            "history_search" => ["query", "before", "offset", "limit"],
            "history_read" => ["sequence", "userOffset", "assistantOffset", "maxBytes"],
            _ => throw new ArgumentException("Outil inconnu.")
        };
        var keys = arguments.EnumerateObject().Select(p => p.Name).ToArray();
        if (keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal)) || keys.Distinct().Count() != keys.Length)
            throw new ArgumentException("Argument inconnu ou dupliqué.");
    }

    public static int Int(JsonElement args, string name, int fallback, int min, int max)
    {
        if (!args.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result) || result < min || result > max)
            throw new ArgumentException($"{name} doit être un entier entre {min} et {max}.");
        return result;
    }
    public static string Json(object value) => JsonSerializer.Serialize(value, ProjectToolCatalog.Json);
    public static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);
}

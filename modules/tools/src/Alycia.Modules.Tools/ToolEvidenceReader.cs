using System.Text.Json;

namespace Alycia.Modules.Tools;

internal static class ToolEvidenceReader
{
    public static IReadOnlyList<EvidenceText> Read(ProjectToolResult result)
    {
        if (result.IsError) return [];
        var allowed = (result.EvidenceItems ?? []).Select(e => e.Reference)
            .Concat(result.Evidence is null ? [] : new[] { result.Evidence.Reference })
            .Concat((result.History ?? []).Select(e => e.Reference)).Where(r => r is not null).ToHashSet(StringComparer.Ordinal);
        var found = new List<EvidenceText>();
        void Visit(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (node.TryGetProperty("reference", out var reference) && reference.ValueKind == JsonValueKind.String
                    && allowed.Contains(reference.GetString()) && node.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    found.Add(new(reference.GetString()!, text.GetString()!, (result.History ?? []).FirstOrDefault(h => h.Reference == reference.GetString())?.Role));
                foreach (var property in node.EnumerateObject()) Visit(property.Value);
            }
            else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) Visit(item);
        }
        try { using var parsed = JsonDocument.Parse(result.Text); Visit(parsed.RootElement); }
        catch (JsonException) { }
        return found;
    }
}

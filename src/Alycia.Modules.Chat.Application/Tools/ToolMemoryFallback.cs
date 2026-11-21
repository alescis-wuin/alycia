using Alycia.Contracts.Documents;
using System.Text;

namespace Alycia.Modules.Chat.Application;

// Deliberately lossy, verbatim extracts; never presented as a successful semantic summary.
public static class ToolMemoryFallback
{
    public const string Warning = "Le résumé des anciens résultats d’outils est vide. L’exploration continue avec des extraits partiels ; certaines informations anciennes doivent être relues.";
    public static string Extract(IReadOnlyList<SummarySource> sources, int maxBytes)
    {
        const string label = "REPLI EXTRACTIF : portions omises, aucun résumé validé. Relire les sources nécessaires.\n";
        var text = new StringBuilder(Utf8Text.Prefix(label, maxBytes));
        var remaining = maxBytes - Encoding.UTF8.GetByteCount(text.ToString());
        var selected = sources.Where(s => !string.IsNullOrWhiteSpace(s.Text)).TakeLast(3).ToArray();
        for (var i = 0; i < selected.Length && remaining > 32; i++)
        {
            var quota = remaining / (selected.Length - i);
            var header = "[" + Utf8Text.Prefix(selected[i].Key, Math.Min(48, quota / 3)) + "] ";
            var excerpt = Utf8Text.Prefix(selected[i].Text, Math.Max(0, quota - Encoding.UTF8.GetByteCount(header) - 2));
            var block = header + excerpt + "\n";
            text.Append(block); remaining -= Encoding.UTF8.GetByteCount(block);
        }
        return text.ToString();
    }
}

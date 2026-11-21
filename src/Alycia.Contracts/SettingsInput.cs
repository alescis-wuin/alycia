using System.Text;

namespace Alycia.Contracts.Chat;

public static class SettingsInput
{
    public static int Estimate(string text) => Encoding.UTF8.GetByteCount(text) + 16;

    public static void ValidateWindow(string? model, string? system, string? notes, ChatSettings settings, bool canonical = false)
    {
        var name = model?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Contains('\0'))
            throw new ArgumentException("Le modèle doit contenir entre 1 et 200 caractères, sans caractère nul.");
        var instruction = Optional(system, 4000);
        var context = Optional(notes, 8000);
        if (canonical && (name != model || instruction != system || context != notes))
            throw new ArgumentException("Les textes de configuration contiennent des espaces de bord non canoniques.");
        if (context.Length > 0) instruction += "\n\nNotes de référence fournies par l’utilisateur :\n" + context;
        var used = Estimate("Bonjour") + (instruction.Length == 0 ? 0 : Estimate(instruction));
        if (used > settings.ContextTokens - settings.MaxOutputTokens - settings.SafetyTokens)
            throw new ArgumentException("Le message et l’instruction système dépassent le budget de contexte. Raccourcissez-les.");
    }

    private static string Optional(string? value, int limit)
    {
        var text = value?.Trim() ?? "";
        if (text.Length > limit || text.Contains('\0'))
            throw new ArgumentException($"L’instruction système est limitée à {limit} caractères, sans caractère nul.");
        return text;
    }
}

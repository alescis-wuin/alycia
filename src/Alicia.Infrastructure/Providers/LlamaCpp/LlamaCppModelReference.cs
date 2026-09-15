namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal static class LlamaCppModelReference
{
    public static string Normalize(string modelReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelReference);

        string normalized = modelReference.Trim();

        if (normalized.Length > 320)
        {
            throw new ArgumentException(
                "Hugging Face model reference cannot exceed 320 characters.",
                nameof(modelReference));
        }

        if (normalized.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Hugging Face model reference cannot contain whitespace.",
                nameof(modelReference));
        }

        int quantifierIndex = normalized.IndexOf(':');

        if (quantifierIndex >= 0
            && normalized.IndexOf(':', quantifierIndex + 1) >= 0)
        {
            throw new ArgumentException(
                "Hugging Face model reference can contain at most one quantization separator ':'.",
                nameof(modelReference));
        }

        string repository = quantifierIndex >= 0
            ? normalized[..quantifierIndex]
            : normalized;
        string? quantifier = quantifierIndex >= 0
            ? normalized[(quantifierIndex + 1)..]
            : null;

        if (!IsValidRepositoryId(repository))
        {
            throw new ArgumentException(
                "Hugging Face model reference must use a valid 'owner/model[:quant]' repository identifier.",
                nameof(modelReference));
        }

        if (quantifierIndex >= 0 && !IsValidQuantifier(quantifier!))
        {
            throw new ArgumentException(
                "Hugging Face quantization contains unsupported characters after ':'.",
                nameof(modelReference));
        }

        return normalized;
    }

    private static bool IsValidRepositoryId(string repository)
    {
        if (repository.Length is 0 or > 256)
        {
            return false;
        }

        int slashCount = 0;
        bool previousWasSpecial = true;

        foreach (char character in repository)
        {
            if (char.IsAsciiLetterOrDigit(character) || character == '_')
            {
                previousWasSpecial = false;
                continue;
            }

            bool isSpecial = character is '/' or '.' or '-';

            if (!isSpecial || previousWasSpecial)
            {
                return false;
            }

            if (character == '/')
            {
                slashCount++;
            }

            previousWasSpecial = true;
        }

        return !previousWasSpecial && slashCount == 1;
    }

    private static bool IsValidQuantifier(string quantifier)
    {
        return quantifier.Length is > 0 and <= 63
            && quantifier.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '_' or '.' or '-');
    }
}

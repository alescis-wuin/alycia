namespace Alicia.Application.Generations;

public readonly record struct GenerationProfileModelScope
{
    public GenerationProfileModelScope(string providerId, string modelReference)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException(
                "Provider identifier cannot be empty or whitespace.",
                nameof(providerId));
        }

        if (string.IsNullOrWhiteSpace(modelReference))
        {
            throw new ArgumentException(
                "Model reference cannot be empty or whitespace.",
                nameof(modelReference));
        }

        ProviderId = providerId.Trim();
        ModelReference = modelReference.Trim();
    }

    public string ProviderId { get; }

    public string ModelReference { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(ProviderId)
        || string.IsNullOrWhiteSpace(ModelReference);

    public override string ToString()
    {
        return IsEmpty ? string.Empty : $"{ProviderId}:{ModelReference}";
    }
}

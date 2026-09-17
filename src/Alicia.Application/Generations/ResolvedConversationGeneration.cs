namespace Alicia.Application.Generations;

public sealed record ResolvedConversationGeneration
{
    public ResolvedConversationGeneration(
        GenerationSnapshot snapshot,
        string? systemInstructions)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Snapshot = snapshot;
        SystemInstructions = string.IsNullOrWhiteSpace(systemInstructions)
            ? null
            : systemInstructions;
    }

    public GenerationSnapshot Snapshot { get; }

    public string? SystemInstructions { get; }
}

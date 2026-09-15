namespace Alicia.Application.Conversations;

public enum ConversationResponseChunkKind
{
    Content = 1,
    Reasoning = 2,
}

public sealed record ConversationResponseChunk
{
    public ConversationResponseChunk(string contentDelta)
        : this(ConversationResponseChunkKind.Content, contentDelta)
    {
    }

    public ConversationResponseChunk(
        ConversationResponseChunkKind kind,
        string textDelta)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Response chunk kind is not defined.");
        }

        ArgumentNullException.ThrowIfNull(textDelta);

        if (textDelta.Length == 0)
        {
            throw new ArgumentException(
                "Response chunk text cannot be empty.",
                nameof(textDelta));
        }

        Kind = kind;
        TextDelta = textDelta;
    }

    public ConversationResponseChunkKind Kind { get; }

    public string TextDelta { get; }

    public string ContentDelta => TextDelta;
}

namespace Alicia.Application.Conversations;

public sealed record ConversationResponseChunk
{
    public ConversationResponseChunk(string contentDelta)
    {
        ArgumentNullException.ThrowIfNull(contentDelta);

        if (contentDelta.Length == 0)
        {
            throw new ArgumentException(
                "Response chunk content cannot be empty.",
                nameof(contentDelta));
        }

        ContentDelta = contentDelta;
    }

    public string ContentDelta { get; }
}

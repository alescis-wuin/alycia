namespace Alicia.Application.Conversations;

public sealed record ConversationResponse
{
    public ConversationResponse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException(
                "Conversation response content cannot be empty or whitespace.",
                nameof(content));
        }

        Content = content;
    }

    public string Content { get; }
}

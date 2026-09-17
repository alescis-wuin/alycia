using Alicia.Domain.Conversations;

namespace Alicia.Application.Generations;

public sealed record ConversationGenerationSelection
{
    public ConversationGenerationSelection(
        ConversationId conversationId,
        GenerationProfileModelScope modelScope,
        GenerationProfileId profileId)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }

        if (modelScope.IsEmpty)
        {
            throw new ArgumentException(
                "Generation-profile model scope cannot be empty.",
                nameof(modelScope));
        }

        if (profileId.IsEmpty)
        {
            throw new ArgumentException(
                "Generation-profile identifier cannot be empty.",
                nameof(profileId));
        }

        ConversationId = conversationId;
        ModelScope = modelScope;
        ProfileId = profileId;
    }

    public ConversationId ConversationId { get; }

    public GenerationProfileModelScope ModelScope { get; }

    public GenerationProfileId ProfileId { get; }
}

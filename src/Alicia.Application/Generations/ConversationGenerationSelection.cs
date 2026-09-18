using Alicia.Domain.Conversations;

namespace Alicia.Application.Generations;

public sealed record ConversationGenerationSelection
{
    public ConversationGenerationSelection(
        ConversationId conversationId,
        GenerationProfileModelScope modelScope,
        GenerationProfileId profileId)
        : this(
            conversationId,
            (ConversationBranchId?)null,
            modelScope,
            profileId)
    {
    }

    public ConversationGenerationSelection(
        ConversationId conversationId,
        ConversationBranchId branchId,
        GenerationProfileModelScope modelScope,
        GenerationProfileId profileId)
        : this(
            conversationId,
            (ConversationBranchId?)branchId,
            modelScope,
            profileId)
    {
    }

    private ConversationGenerationSelection(
        ConversationId conversationId,
        ConversationBranchId? branchId,
        GenerationProfileModelScope modelScope,
        GenerationProfileId profileId)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }

        if (branchId is ConversationBranchId scopedBranchId && scopedBranchId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation branch identifier cannot be empty.",
                nameof(branchId));
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
        BranchId = branchId;
        ModelScope = modelScope;
        ProfileId = profileId;
    }

    public ConversationId ConversationId { get; }

    public ConversationBranchId? BranchId { get; }

    public bool IsBranchScoped => BranchId.HasValue;

    public GenerationProfileModelScope ModelScope { get; }

    public GenerationProfileId ProfileId { get; }

    public ConversationGenerationSelection ForBranch(ConversationBranchId branchId)
    {
        return new ConversationGenerationSelection(
            ConversationId,
            branchId,
            ModelScope,
            ProfileId);
    }
}

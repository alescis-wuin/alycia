using System.Collections.ObjectModel;

namespace Alicia.Domain.Conversations;

public sealed class Conversation
{
    public const string DefaultTitle = "New conversation";
    public const int MaxTitleLength = 120;

    private readonly List<ChatMessage> _messageRevisions = [];
    private readonly ReadOnlyCollection<ChatMessage> _readOnlyMessageRevisions;
    private readonly Dictionary<MessageRevisionId, ChatMessage> _messageRevisionsById = [];
    private readonly List<ConversationContextRevision> _contextRevisions = [];
    private readonly ReadOnlyCollection<ConversationContextRevision> _readOnlyContextRevisions;
    private readonly Dictionary<ConversationContextRevisionId, ConversationContextRevision>
        _contextRevisionsById = [];
    private readonly List<ConversationBranch> _branches = [];
    private readonly ReadOnlyCollection<ConversationBranch> _readOnlyBranches;
    private readonly Dictionary<ConversationBranchId, int> _branchIndexes = [];
    private readonly HashSet<MessageRevisionId> _ownedRevisionIds = [];
    private readonly HashSet<ConversationContextRevisionId> _ownedContextRevisionIds = [];
    private readonly List<ChatMessage> _messages = [];
    private readonly ReadOnlyCollection<ChatMessage> _readOnlyMessages;

    public Conversation(ConversationId id, DateTimeOffset createdAt)
        : this(id, DefaultTitle, createdAt, createdAt, createRootBranch: true)
    {
    }

    public Conversation(
        ConversationId id,
        string title,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
        : this(id, title, createdAt, updatedAt, createRootBranch: true)
    {
    }

    private Conversation(
        ConversationId id,
        string title,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool createRootBranch)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("Conversation identifier cannot be empty.", nameof(id));
        }

        if (createdAt == default)
        {
            throw new ArgumentOutOfRangeException(nameof(createdAt), createdAt, "Conversation creation time must be defined.");
        }

        if (updatedAt == default)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAt), updatedAt, "Conversation update time must be defined.");
        }

        if (updatedAt < createdAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(updatedAt),
                updatedAt,
                "Conversation update time cannot predate its creation time.");
        }

        Id = id;
        Title = NormalizeTitle(title);
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        _readOnlyMessageRevisions = _messageRevisions.AsReadOnly();
        _readOnlyContextRevisions = _contextRevisions.AsReadOnly();
        _readOnlyBranches = _branches.AsReadOnly();
        _readOnlyMessages = _messages.AsReadOnly();

        if (createRootBranch)
        {
            ConversationBranch rootBranch = new(
                ConversationBranchId.New(),
                parentBranchId: null,
                forkedAfterRevisionId: null,
                localRevisionIds: Array.Empty<MessageRevisionId>());
            AddBranchUnchecked(rootBranch);
            ActiveBranchId = rootBranch.Id;
        }
    }

    public ConversationId Id { get; }

    public ConversationContextId ContextId => new(Id.Value);

    public string Title { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public ConversationBranchId ActiveBranchId { get; private set; }

    public IReadOnlyList<ChatMessage> Messages => _readOnlyMessages;

    public IReadOnlyList<ChatMessage> MessageRevisions => _readOnlyMessageRevisions;

    public IReadOnlyList<ConversationContextRevision> ContextRevisions => _readOnlyContextRevisions;

    public IReadOnlyList<ConversationBranch> Branches => _readOnlyBranches;

    public ConversationContextRevision? ActiveContextRevision =>
        ResolveBranchContextRevision(ActiveBranchId);

    public static Conversation Restore(
        ConversationId id,
        string title,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        IEnumerable<ChatMessage> messageRevisions,
        IEnumerable<ConversationBranch> branches,
        ConversationBranchId activeBranchId)
    {
        return Restore(
            id,
            title,
            createdAt,
            updatedAt,
            messageRevisions,
            Array.Empty<ConversationContextRevision>(),
            branches,
            activeBranchId);
    }

    public static Conversation Restore(
        ConversationId id,
        string title,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        IEnumerable<ChatMessage> messageRevisions,
        IEnumerable<ConversationContextRevision> contextRevisions,
        IEnumerable<ConversationBranch> branches,
        ConversationBranchId activeBranchId)
    {
        ArgumentNullException.ThrowIfNull(messageRevisions);
        ArgumentNullException.ThrowIfNull(contextRevisions);
        ArgumentNullException.ThrowIfNull(branches);

        Conversation conversation = new(
            id,
            title,
            createdAt,
            updatedAt,
            createRootBranch: false);

        foreach (ChatMessage revision in messageRevisions)
        {
            conversation.RegisterRevision(revision);
        }

        foreach (ConversationContextRevision revision in contextRevisions)
        {
            conversation.RegisterContextRevision(revision);
        }

        foreach (ConversationBranch branch in branches)
        {
            conversation.RegisterBranch(branch);
        }

        conversation.ValidateRestoredGraph(activeBranchId);
        conversation.ActiveBranchId = activeBranchId;
        conversation.RebuildActiveMessages();
        return conversation;
    }

    public void Rename(string title, DateTimeOffset renamedAt)
    {
        if (renamedAt == default)
        {
            throw new ArgumentOutOfRangeException(nameof(renamedAt), renamedAt, "Rename time must be defined.");
        }

        if (renamedAt < UpdatedAt)
        {
            throw new InvalidOperationException("A conversation cannot be renamed in the past.");
        }

        Title = NormalizeTitle(title);
        UpdatedAt = renamedAt;
    }

    public void AddMessage(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_messages.Any(existing => existing.Id == message.Id))
        {
            throw new InvalidOperationException(
                $"Message '{message.Id}' already belongs to the active conversation branch.");
        }

        RegisterRevision(message);

        ConversationBranch activeBranch = GetBranch(ActiveBranchId);
        ReplaceBranch(activeBranch.Append(message.RevisionId));
        _ownedRevisionIds.Add(message.RevisionId);
        _messages.Add(message);

        if (message.CreatedAt > UpdatedAt)
        {
            UpdatedAt = message.CreatedAt;
        }
    }

    public ConversationContextRevision UpdateContext(
        string? instructions,
        bool replaceProfileInstructions,
        DateTimeOffset updatedAt)
    {
        if (updatedAt == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(updatedAt),
                updatedAt,
                "Conversation-context update time must be defined.");
        }

        if (updatedAt < UpdatedAt)
        {
            throw new InvalidOperationException("Conversation context cannot be updated in the past.");
        }

        ConversationContextRevision? current = ActiveContextRevision;
        ConversationContextRevision revision = new(
            ContextId,
            ConversationContextRevisionId.New(),
            current?.RevisionId,
            instructions,
            replaceProfileInstructions,
            updatedAt);
        RegisterContextRevision(revision);

        MessageRevisionId? appliedAfterRevisionId = _messages.Count == 0
            ? null
            : _messages[^1].RevisionId;
        ConversationContextBinding binding = new(
            revision.RevisionId,
            appliedAfterRevisionId);
        ConversationBranch activeBranch = GetBranch(ActiveBranchId);
        ConversationBranch updatedBranch = activeBranch.AppendContext(binding);
        ReplaceBranch(updatedBranch);
        _ownedContextRevisionIds.Add(revision.RevisionId);
        UpdatedAt = updatedAt;
        return revision;
    }

    public ChatMessage EditUserMessage(
        MessageRevisionId messageRevisionId,
        string content,
        DateTimeOffset editedAt)
    {
        if (messageRevisionId.IsEmpty)
        {
            throw new ArgumentException(
                "Message revision identifier cannot be empty.",
                nameof(messageRevisionId));
        }

        if (editedAt == default)
        {
            throw new ArgumentOutOfRangeException(nameof(editedAt), editedAt, "Edit time must be defined.");
        }

        if (editedAt < UpdatedAt)
        {
            throw new InvalidOperationException("A conversation message cannot be edited in the past.");
        }

        int targetIndex = _messages.FindIndex(message => message.RevisionId == messageRevisionId);
        if (targetIndex < 0)
        {
            throw new KeyNotFoundException(
                $"Message revision '{messageRevisionId}' does not belong to the active conversation branch.");
        }

        ChatMessage target = _messages[targetIndex];
        if (target.Role != MessageRole.User)
        {
            throw new InvalidOperationException("Only a user message can be edited into a new conversation branch.");
        }

        ChatMessage editedRevision = new(
            target.Id,
            MessageRevisionId.New(),
            target.RevisionId,
            target.Role,
            content,
            editedAt);
        ValidateRevisionForRegistration(editedRevision);

        MessageRevisionId? forkedAfterRevisionId = targetIndex == 0
            ? null
            : _messages[targetIndex - 1].RevisionId;
        ConversationContextRevision? inheritedContext =
            ResolveBranchContextBeforeMessageIndex(ActiveBranchId, targetIndex);
        ConversationBranch childBranch = new(
            ConversationBranchId.New(),
            ActiveBranchId,
            forkedAfterRevisionId,
            [editedRevision.RevisionId],
            inheritedContext?.RevisionId,
            contextBindings: null);

        RegisterRevisionUnchecked(editedRevision);
        AddBranchUnchecked(childBranch);
        _ownedRevisionIds.Add(editedRevision.RevisionId);
        ActiveBranchId = childBranch.Id;
        RebuildActiveMessages();
        UpdatedAt = editedAt;
        return editedRevision;
    }

    public void ActivateBranch(ConversationBranchId branchId)
    {
        if (branchId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation branch identifier cannot be empty.",
                nameof(branchId));
        }

        if (!_branchIndexes.ContainsKey(branchId))
        {
            throw new KeyNotFoundException(
                $"Conversation branch '{branchId}' does not belong to conversation '{Id}'.");
        }

        ActiveBranchId = branchId;
        RebuildActiveMessages();
    }

    public IReadOnlyList<ChatMessage> GetMessages(ConversationBranchId branchId)
    {
        if (branchId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation branch identifier cannot be empty.",
                nameof(branchId));
        }

        return Array.AsReadOnly(ResolveBranchMessages(branchId));
    }

    public ConversationContextRevision? GetContextRevision(ConversationBranchId branchId)
    {
        if (branchId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation branch identifier cannot be empty.",
                nameof(branchId));
        }

        return ResolveBranchContextRevision(branchId);
    }

    private void RegisterRevision(ChatMessage revision)
    {
        ValidateRevisionForRegistration(revision);
        RegisterRevisionUnchecked(revision);
    }

    private void ValidateRevisionForRegistration(ChatMessage revision)
    {
        ArgumentNullException.ThrowIfNull(revision);

        if (revision.CreatedAt < CreatedAt)
        {
            throw new InvalidOperationException("A message cannot predate its conversation.");
        }

        if (_messageRevisionsById.ContainsKey(revision.RevisionId))
        {
            throw new InvalidOperationException(
                $"Message revision '{revision.RevisionId}' already belongs to the conversation.");
        }

        if (revision.ParentRevisionId is null)
        {
            if (_messageRevisions.Any(existing => existing.Id == revision.Id))
            {
                throw new InvalidOperationException(
                    $"Message '{revision.Id}' already has a root revision in the conversation.");
            }

            return;
        }

        if (!_messageRevisionsById.TryGetValue(
                revision.ParentRevisionId.Value,
                out ChatMessage? parentRevision))
        {
            throw new InvalidOperationException(
                $"Parent message revision '{revision.ParentRevisionId}' does not belong to the conversation.");
        }

        if (parentRevision.Id != revision.Id)
        {
            throw new InvalidOperationException(
                "A message revision parent must belong to the same logical message.");
        }

        if (parentRevision.Role != revision.Role)
        {
            throw new InvalidOperationException(
                "A message revision cannot change the logical message role.");
        }

        if (revision.CreatedAt < parentRevision.CreatedAt)
        {
            throw new InvalidOperationException(
                "A message revision cannot predate its parent revision.");
        }
    }

    private void RegisterRevisionUnchecked(ChatMessage revision)
    {
        _messageRevisions.Add(revision);
        _messageRevisionsById.Add(revision.RevisionId, revision);
    }

    private void RegisterContextRevision(ConversationContextRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);

        if (revision.ContextId != ContextId)
        {
            throw new InvalidOperationException(
                $"Conversation-context revision '{revision.RevisionId}' belongs to a different conversation context.");
        }

        if (revision.CreatedAt < CreatedAt)
        {
            throw new InvalidOperationException(
                "A conversation-context revision cannot predate its conversation.");
        }

        if (_contextRevisionsById.ContainsKey(revision.RevisionId))
        {
            throw new InvalidOperationException(
                $"Conversation-context revision '{revision.RevisionId}' already belongs to the conversation.");
        }

        if (revision.ParentRevisionId is ConversationContextRevisionId parentRevisionId)
        {
            if (!_contextRevisionsById.TryGetValue(
                    parentRevisionId,
                    out ConversationContextRevision? parentRevision))
            {
                throw new InvalidOperationException(
                    $"Parent conversation-context revision '{parentRevisionId}' does not belong to the conversation.");
            }

            if (parentRevision.ContextId != revision.ContextId)
            {
                throw new InvalidOperationException(
                    "A conversation-context revision parent must belong to the same logical context.");
            }

            if (revision.CreatedAt < parentRevision.CreatedAt)
            {
                throw new InvalidOperationException(
                    "A conversation-context revision cannot predate its parent revision.");
            }
        }

        _contextRevisions.Add(revision);
        _contextRevisionsById.Add(revision.RevisionId, revision);
    }

    private void RegisterBranch(ConversationBranch branch)
    {
        ArgumentNullException.ThrowIfNull(branch);

        if (_branchIndexes.ContainsKey(branch.Id))
        {
            throw new InvalidOperationException(
                $"Conversation branch '{branch.Id}' already belongs to the conversation.");
        }

        int sharedPrefixLength = 0;

        if (branch.ParentBranchId is null)
        {
            if (_branches.Any(existing => existing.ParentBranchId is null))
            {
                throw new InvalidOperationException("A conversation can contain only one root branch.");
            }

            if (branch.InheritedContextRevisionId is not null)
            {
                throw new InvalidOperationException(
                    "A root conversation branch cannot inherit a conversation-context revision.");
            }
        }
        else
        {
            if (!_branchIndexes.ContainsKey(branch.ParentBranchId.Value))
            {
                throw new InvalidOperationException(
                    $"Parent conversation branch '{branch.ParentBranchId}' does not belong to the conversation.");
            }

            if (branch.LocalRevisionIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "A non-root conversation branch must own at least one local message revision.");
            }

            ChatMessage[] parentMessages = ResolveBranchMessages(branch.ParentBranchId.Value);

            if (branch.ForkedAfterRevisionId is MessageRevisionId forkRevisionId)
            {
                int forkIndex = Array.FindIndex(
                    parentMessages,
                    message => message.RevisionId == forkRevisionId);
                if (forkIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Fork message revision '{forkRevisionId}' is not visible on the parent conversation branch.");
                }

                sharedPrefixLength = forkIndex + 1;
            }

            if (sharedPrefixLength >= parentMessages.Length)
            {
                throw new InvalidOperationException(
                    "A child conversation branch must replace a message revision from its parent branch.");
            }

            ChatMessage replacedRevision = parentMessages[sharedPrefixLength];
            if (!_messageRevisionsById.TryGetValue(
                    branch.LocalRevisionIds[0],
                    out ChatMessage? firstLocalRevision))
            {
                throw new InvalidOperationException(
                    $"Conversation branch '{branch.Id}' references unknown message revision '{branch.LocalRevisionIds[0]}'.");
            }

            if (firstLocalRevision.ParentRevisionId != replacedRevision.RevisionId
                || firstLocalRevision.Id != replacedRevision.Id)
            {
                throw new InvalidOperationException(
                    "A child conversation branch must begin with a revision of the first parent message omitted by its fork point.");
            }

            ConversationContextRevision? expectedInheritedContext =
                ResolveBranchContextBeforeMessageIndex(
                    branch.ParentBranchId.Value,
                    sharedPrefixLength);
            if (branch.InheritedContextRevisionId != expectedInheritedContext?.RevisionId)
            {
                throw new InvalidOperationException(
                    "A child conversation branch must inherit the context revision effective at its exact divergence point.");
            }
        }

        foreach (MessageRevisionId revisionId in branch.LocalRevisionIds)
        {
            if (!_messageRevisionsById.ContainsKey(revisionId))
            {
                throw new InvalidOperationException(
                    $"Conversation branch '{branch.Id}' references unknown message revision '{revisionId}'.");
            }

            if (_ownedRevisionIds.Contains(revisionId))
            {
                throw new InvalidOperationException(
                    $"Message revision '{revisionId}' is owned by more than one conversation branch.");
            }
        }

        ValidateContextBindings(branch);
        ValidateResolvedBranchPath(branch);
        AddBranchUnchecked(branch);

        foreach (MessageRevisionId revisionId in branch.LocalRevisionIds)
        {
            _ownedRevisionIds.Add(revisionId);
        }

        foreach (ConversationContextBinding binding in branch.ContextBindings)
        {
            _ownedContextRevisionIds.Add(binding.RevisionId);
        }
    }

    private void ValidateContextBindings(ConversationBranch branch)
    {
        ConversationContextRevision? previousContext = branch.InheritedContextRevisionId
            is ConversationContextRevisionId inheritedRevisionId
                ? GetRequiredContextRevision(inheritedRevisionId)
                : null;
        int previousAnchorIndex = -1;

        for (int index = 0; index < branch.ContextBindings.Count; index++)
        {
            ConversationContextBinding binding = branch.ContextBindings[index];
            ConversationContextRevision revision = GetRequiredContextRevision(binding.RevisionId);

            if (_ownedContextRevisionIds.Contains(binding.RevisionId))
            {
                throw new InvalidOperationException(
                    $"Conversation-context revision '{binding.RevisionId}' is owned by more than one branch binding.");
            }

            if (revision.ParentRevisionId != previousContext?.RevisionId)
            {
                throw new InvalidOperationException(
                    "Conversation-context revision parent does not match the context effective immediately before its branch binding.");
            }

            int anchorIndex;
            if (binding.AppliedAfterRevisionId is null)
            {
                if (branch.ParentBranchId is not null)
                {
                    throw new InvalidOperationException(
                        "A child conversation branch cannot bind local context before its first local message; inherited context owns the divergence boundary.");
                }

                anchorIndex = -1;
            }
            else
            {
                anchorIndex = IndexOfLocalRevision(
                    branch,
                    binding.AppliedAfterRevisionId.Value);
                if (anchorIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Conversation branch '{branch.Id}' context binding references a message revision that is not local to the branch.");
                }
            }

            if (anchorIndex < previousAnchorIndex)
            {
                throw new InvalidOperationException(
                    "Conversation branch context bindings must follow branch timeline order.");
            }

            previousAnchorIndex = anchorIndex;
            previousContext = revision;
        }
    }

    private void ValidateResolvedBranchPath(ConversationBranch branch)
    {
        List<ChatMessage> resolved = [];

        if (branch.ParentBranchId is ConversationBranchId parentBranchId)
        {
            ChatMessage[] parentMessages = ResolveBranchMessages(parentBranchId);
            if (branch.ForkedAfterRevisionId is MessageRevisionId forkRevisionId)
            {
                int forkIndex = Array.FindIndex(
                    parentMessages,
                    message => message.RevisionId == forkRevisionId);
                resolved.AddRange(parentMessages[..(forkIndex + 1)]);
            }
        }

        foreach (MessageRevisionId revisionId in branch.LocalRevisionIds)
        {
            resolved.Add(_messageRevisionsById[revisionId]);
        }

        if (resolved.Select(message => message.RevisionId).Distinct().Count() != resolved.Count)
        {
            throw new InvalidOperationException(
                $"Conversation branch '{branch.Id}' resolves the same message revision more than once.");
        }

        if (resolved.Select(message => message.Id).Distinct().Count() != resolved.Count)
        {
            throw new InvalidOperationException(
                $"Conversation branch '{branch.Id}' resolves more than one revision of the same logical message.");
        }
    }

    private void ValidateRestoredGraph(ConversationBranchId activeBranchId)
    {
        if (_branches.Count == 0)
        {
            throw new InvalidOperationException("A conversation must contain a root branch.");
        }

        if (!_branchIndexes.ContainsKey(activeBranchId))
        {
            throw new InvalidOperationException(
                $"Active conversation branch '{activeBranchId}' does not belong to the conversation.");
        }

        if (_ownedRevisionIds.Count != _messageRevisions.Count)
        {
            throw new InvalidOperationException(
                "Every persisted message revision must be owned by exactly one conversation branch.");
        }

        if (_ownedContextRevisionIds.Count != _contextRevisions.Count)
        {
            throw new InvalidOperationException(
                "Every persisted conversation-context revision must be owned by exactly one branch binding.");
        }

        if (_messageRevisions.Any(revision => revision.CreatedAt > UpdatedAt))
        {
            throw new InvalidOperationException(
                "Conversation update time predates a persisted message revision.");
        }

        if (_contextRevisions.Any(revision => revision.CreatedAt > UpdatedAt))
        {
            throw new InvalidOperationException(
                "Conversation update time predates a persisted context revision.");
        }
    }

    private void AddBranchUnchecked(ConversationBranch branch)
    {
        _branchIndexes.Add(branch.Id, _branches.Count);
        _branches.Add(branch);
    }

    private void ReplaceBranch(ConversationBranch branch)
    {
        int index = _branchIndexes[branch.Id];
        _branches[index] = branch;
    }

    private ConversationBranch GetBranch(ConversationBranchId branchId)
    {
        if (!_branchIndexes.TryGetValue(branchId, out int index))
        {
            throw new KeyNotFoundException(
                $"Conversation branch '{branchId}' does not belong to conversation '{Id}'.");
        }

        return _branches[index];
    }

    private ConversationContextRevision GetRequiredContextRevision(
        ConversationContextRevisionId revisionId)
    {
        if (!_contextRevisionsById.TryGetValue(
                revisionId,
                out ConversationContextRevision? revision))
        {
            throw new InvalidOperationException(
                $"Conversation branch references unknown context revision '{revisionId}'.");
        }

        return revision;
    }

    private ChatMessage[] ResolveBranchMessages(ConversationBranchId branchId)
    {
        ConversationBranch branch = GetBranch(branchId);
        List<ChatMessage> resolved = [];

        if (branch.ParentBranchId is ConversationBranchId parentBranchId)
        {
            ChatMessage[] parentMessages = ResolveBranchMessages(parentBranchId);

            if (branch.ForkedAfterRevisionId is MessageRevisionId forkRevisionId)
            {
                int forkIndex = Array.FindIndex(
                    parentMessages,
                    message => message.RevisionId == forkRevisionId);
                if (forkIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Fork message revision '{forkRevisionId}' is not visible on the parent conversation branch.");
                }

                resolved.AddRange(parentMessages[..(forkIndex + 1)]);
            }
        }

        foreach (MessageRevisionId revisionId in branch.LocalRevisionIds)
        {
            resolved.Add(_messageRevisionsById[revisionId]);
        }

        return resolved.ToArray();
    }

    private ConversationContextRevision? ResolveBranchContextRevision(
        ConversationBranchId branchId)
    {
        ChatMessage[] messages = ResolveBranchMessages(branchId);
        return ResolveBranchContextBeforeMessageIndex(branchId, messages.Length);
    }

    private ConversationContextRevision? ResolveBranchContextBeforeMessageIndex(
        ConversationBranchId branchId,
        int targetIndex)
    {
        ConversationBranch branch = GetBranch(branchId);
        ChatMessage[] resolvedMessages = ResolveBranchMessages(branchId);

        if (targetIndex < 0 || targetIndex > resolvedMessages.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetIndex),
                targetIndex,
                "Context resolution index must belong to the resolved branch timeline.");
        }

        int sharedPrefixLength = GetSharedPrefixLength(branch);
        if (branch.ParentBranchId is ConversationBranchId parentBranchId
            && targetIndex <= sharedPrefixLength)
        {
            return ResolveBranchContextBeforeMessageIndex(parentBranchId, targetIndex);
        }

        ConversationContextRevision? current = branch.InheritedContextRevisionId
            is ConversationContextRevisionId inheritedRevisionId
                ? GetRequiredContextRevision(inheritedRevisionId)
                : null;

        foreach (ConversationContextBinding binding in branch.ContextBindings)
        {
            int anchorIndex = binding.AppliedAfterRevisionId is null
                ? -1
                : Array.FindIndex(
                    resolvedMessages,
                    message => message.RevisionId == binding.AppliedAfterRevisionId.Value);

            if (anchorIndex < 0 && binding.AppliedAfterRevisionId is not null)
            {
                throw new InvalidOperationException(
                    $"Conversation branch '{branch.Id}' contains an invalid context binding anchor.");
            }

            if (anchorIndex >= targetIndex)
            {
                break;
            }

            current = GetRequiredContextRevision(binding.RevisionId);
        }

        return current;
    }

    private int GetSharedPrefixLength(ConversationBranch branch)
    {
        if (branch.ParentBranchId is not ConversationBranchId parentBranchId)
        {
            return 0;
        }

        if (branch.ForkedAfterRevisionId is null)
        {
            return 0;
        }

        ChatMessage[] parentMessages = ResolveBranchMessages(parentBranchId);
        int forkIndex = Array.FindIndex(
            parentMessages,
            message => message.RevisionId == branch.ForkedAfterRevisionId.Value);
        if (forkIndex < 0)
        {
            throw new InvalidOperationException(
                $"Fork message revision '{branch.ForkedAfterRevisionId}' is not visible on the parent conversation branch.");
        }

        return forkIndex + 1;
    }

    private static int IndexOfLocalRevision(
        ConversationBranch branch,
        MessageRevisionId revisionId)
    {
        for (int index = 0; index < branch.LocalRevisionIds.Count; index++)
        {
            if (branch.LocalRevisionIds[index] == revisionId)
            {
                return index;
            }
        }

        return -1;
    }

    private void RebuildActiveMessages()
    {
        _messages.Clear();
        _messages.AddRange(ResolveBranchMessages(ActiveBranchId));
    }

    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Conversation title cannot be empty or whitespace.", nameof(title));
        }

        string normalizedTitle = title.Trim();

        if (normalizedTitle.Length > MaxTitleLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(title),
                normalizedTitle.Length,
                $"Conversation title cannot exceed {MaxTitleLength} characters.");
        }

        if (normalizedTitle.Any(char.IsControl))
        {
            throw new ArgumentException("Conversation title cannot contain control characters.", nameof(title));
        }

        return normalizedTitle;
    }
}

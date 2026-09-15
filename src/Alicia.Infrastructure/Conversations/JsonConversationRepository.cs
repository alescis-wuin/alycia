using System.Text.Json;
using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Infrastructure.Conversations;

public sealed class JsonConversationRepository : IConversationRepository
{
    private const string FileExtension = ".json";
    private const int CurrentSchemaVersion = 2;
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _directoryPath;

    public JsonConversationRepository(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("Conversation storage directory cannot be empty or whitespace.", nameof(directoryPath));
        }

        _directoryPath = Path.GetFullPath(directoryPath);
    }

    public Task<bool> DeleteAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken)
    {
        ValidateConversationId(conversationId);
        cancellationToken.ThrowIfCancellationRequested();

        string filePath = GetFilePath(conversationId);

        if (!File.Exists(filePath))
        {
            return Task.FromResult(false);
        }

        File.Delete(filePath);
        return Task.FromResult(true);
    }

    public async Task<Conversation?> FindAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken)
    {
        ValidateConversationId(conversationId);
        cancellationToken.ThrowIfCancellationRequested();

        string filePath = GetFilePath(conversationId);

        if (!File.Exists(filePath))
        {
            return null;
        }

        return await ReadConversationAsync(filePath, conversationId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_directoryPath))
        {
            return [];
        }

        List<ConversationSummary> conversations = [];

        foreach (string filePath in Directory.EnumerateFiles(
            _directoryPath,
            "*" + FileExtension,
            SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetConversationId(filePath, out ConversationId conversationId))
            {
                continue;
            }

            Conversation conversation = await ReadConversationAsync(
                filePath,
                conversationId,
                cancellationToken).ConfigureAwait(false);

            conversations.Add(ConversationSummary.FromConversation(conversation));
        }

        return conversations
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .ThenByDescending(conversation => conversation.CreatedAt)
            .ThenBy(conversation => conversation.Id.Value)
            .ToArray();
    }

    public async Task SaveAsync(
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_directoryPath);

        string targetPath = GetFilePath(conversation.Id);
        string temporaryPath = Path.Combine(
            _directoryPath,
            $".{conversation.Id.Value:N}.{Guid.NewGuid():N}.tmp");

        try
        {
            ConversationDocument document = FromDomain(conversation);

            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer
                    .SerializeAsync(stream, document, _serializerOptions, cancellationToken)
                    .ConfigureAwait(false);

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static ConversationDocument FromDomain(Conversation conversation)
    {
        List<MessageDocument> messages = conversation.Messages
            .Select(message => new MessageDocument
            {
                Id = message.Id.Value,
                Role = (int)message.Role,
                Content = message.Content,
                CreatedAt = message.CreatedAt,
            })
            .ToList();

        return new ConversationDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Id = conversation.Id.Value,
            Title = conversation.Title,
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt,
            Messages = messages,
        };
    }

    private static DateTimeOffset GetLegacyUpdatedAt(ConversationDocument document)
    {
        DateTimeOffset updatedAt = document.CreatedAt;

        if (document.Messages is null)
        {
            return updatedAt;
        }

        foreach (MessageDocument message in document.Messages)
        {
            if (message.CreatedAt > updatedAt)
            {
                updatedAt = message.CreatedAt;
            }
        }

        return updatedAt;
    }

    private string GetFilePath(ConversationId conversationId)
    {
        return Path.Combine(_directoryPath, conversationId.Value.ToString("N") + FileExtension);
    }

    private static async Task<Conversation> ReadConversationAsync(
        string filePath,
        ConversationId expectedId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            ConversationDocument? document = await JsonSerializer
                .DeserializeAsync<ConversationDocument>(stream, _serializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (document is null)
            {
                throw new InvalidDataException($"Conversation file '{filePath}' does not contain a document.");
            }

            if (document.Id != expectedId.Value)
            {
                throw new InvalidDataException(
                    $"Conversation file '{filePath}' contains identifier '{document.Id}' instead of '{expectedId.Value}'.");
            }

            return ToDomain(document);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Conversation file '{filePath}' contains invalid JSON.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Conversation file '{filePath}' contains invalid conversation data.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException($"Conversation file '{filePath}' violates conversation invariants.", exception);
        }
    }

    private static Conversation ToDomain(ConversationDocument document)
    {
        if (document.SchemaVersion is not 0 and not CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Conversation schema version '{document.SchemaVersion}' is not supported.");
        }

        if (document.Messages is null)
        {
            throw new InvalidDataException("Conversation document does not contain a message collection.");
        }

        string title = string.IsNullOrWhiteSpace(document.Title)
            ? Conversation.DefaultTitle
            : document.Title;

        DateTimeOffset inferredUpdatedAt = GetLegacyUpdatedAt(document);
        DateTimeOffset updatedAt = document.UpdatedAt == default
            ? inferredUpdatedAt
            : document.UpdatedAt;

        if (updatedAt < inferredUpdatedAt)
        {
            throw new InvalidDataException("Conversation update time predates persisted activity.");
        }

        Conversation conversation = new(
            new ConversationId(document.Id),
            title,
            document.CreatedAt,
            updatedAt);

        foreach (MessageDocument message in document.Messages)
        {
            conversation.AddMessage(new ChatMessage(
                new MessageId(message.Id),
                (MessageRole)message.Role,
                message.Content,
                message.CreatedAt));
        }

        return conversation;
    }

    private static bool TryGetConversationId(
        string filePath,
        out ConversationId conversationId)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);

        if (Guid.TryParseExact(fileName, "N", out Guid value))
        {
            conversationId = new ConversationId(value);
            return !conversationId.IsEmpty;
        }

        conversationId = default;
        return false;
    }

    private static void ValidateConversationId(ConversationId conversationId)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException("Conversation identifier cannot be empty.", nameof(conversationId));
        }
    }

    private sealed class ConversationDocument
    {
        public ConversationDocument()
        {
        }

        public int SchemaVersion { get; init; }

        public Guid Id { get; init; }

        public string? Title { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public List<MessageDocument>? Messages { get; init; } = [];
    }

    private sealed class MessageDocument
    {
        public MessageDocument()
        {
        }

        public Guid Id { get; init; }

        public int Role { get; init; }

        public string Content { get; init; } = string.Empty;

        public DateTimeOffset CreatedAt { get; init; }
    }
}

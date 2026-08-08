using System.Text.Json;
using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Infrastructure.Conversations;

public sealed class JsonConversationRepository : IConversationRepository
{
    private const string FileExtension = ".json";
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

    public async Task<Conversation?> FindAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException("Conversation identifier cannot be empty.", nameof(conversationId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        string filePath = GetFilePath(conversationId);

        if (!File.Exists(filePath))
        {
            return null;
        }

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

    private string GetFilePath(ConversationId conversationId)
    {
        return Path.Combine(_directoryPath, conversationId.Value.ToString("N") + FileExtension);
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
            Id = conversation.Id.Value,
            CreatedAt = conversation.CreatedAt,
            Messages = messages,
        };
    }

    private static Conversation ToDomain(ConversationDocument document)
    {
        if (document.Messages is null)
        {
            throw new InvalidDataException("Conversation document does not contain a message collection.");
        }

        Conversation conversation = new(
            new ConversationId(document.Id),
            document.CreatedAt);

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

    private sealed class ConversationDocument
    {
        public ConversationDocument()
        {
        }

        public Guid Id { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

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

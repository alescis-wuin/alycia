using System.Text.Json;
using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Infrastructure.Generations;

public sealed class JsonGenerationSnapshotStore : IGenerationSnapshotStore
{
    private const int LegacySchemaVersion = 1;
    private const int CurrentSchemaVersion = 2;
    private const string FileExtension = ".json";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _directoryPath;

    public JsonGenerationSnapshotStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = Path.GetFullPath(directoryPath);
    }

    public async Task<GenerationSnapshot?> FindAsync(
        GenerationSnapshotId snapshotId,
        CancellationToken cancellationToken = default)
    {
        ValidateSnapshotId(snapshotId);
        cancellationToken.ThrowIfCancellationRequested();

        string filePath = GetFilePath(snapshotId);
        if (!File.Exists(filePath))
        {
            return null;
        }

        SnapshotDocument document = await ReadDocumentAsync(
                filePath,
                cancellationToken)
            .ConfigureAwait(false);

        if (document.Id != snapshotId.Value)
        {
            throw new InvalidDataException(
                $"Generation snapshot file '{filePath}' contains identifier '{document.Id}' instead of '{snapshotId.Value}'.");
        }

        return MapToSnapshot(document);
    }

    public async Task SaveAsync(
        GenerationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_directoryPath);
        string targetPath = GetFilePath(snapshot.Id);
        if (File.Exists(targetPath))
        {
            throw new InvalidOperationException(
                $"Generation snapshot '{snapshot.Id}' is already persisted and cannot be overwritten.");
        }

        string temporaryPath = Path.Combine(
            _directoryPath,
            $".{snapshot.Id.Value:N}.{Guid.NewGuid():N}.tmp");

        try
        {
            SnapshotDocument document = MapFromSnapshot(snapshot);
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer
                    .SerializeAsync(stream, document, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, targetPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<bool> DeleteAsync(
        GenerationSnapshotId snapshotId,
        CancellationToken cancellationToken = default)
    {
        ValidateSnapshotId(snapshotId);
        cancellationToken.ThrowIfCancellationRequested();

        string filePath = GetFilePath(snapshotId);
        if (!File.Exists(filePath))
        {
            return Task.FromResult(false);
        }

        File.Delete(filePath);
        return Task.FromResult(true);
    }

    private string GetFilePath(GenerationSnapshotId snapshotId)
    {
        return Path.Combine(
            _directoryPath,
            snapshotId.Value.ToString("N") + FileExtension);
    }

    private static void ValidateSnapshotId(GenerationSnapshotId snapshotId)
    {
        if (snapshotId.IsEmpty)
        {
            throw new ArgumentException(
                "Generation-snapshot identifier cannot be empty.",
                nameof(snapshotId));
        }
    }

    private static async Task<SnapshotDocument> ReadDocumentAsync(
        string filePath,
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
            SnapshotDocument? document = await JsonSerializer
                .DeserializeAsync<SnapshotDocument>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (document is null)
            {
                throw new InvalidDataException(
                    $"Generation snapshot file '{filePath}' does not contain a document.");
            }

            if (document.SchemaVersion is not LegacySchemaVersion and not CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported generation snapshot schema version '{document.SchemaVersion}'.");
            }

            if (document.Generation is null)
            {
                throw new InvalidDataException(
                    "Generation snapshot document does not contain generation options.");
            }

            if (document.InputMessageRevisionIds is null)
            {
                throw new InvalidDataException(
                    "Generation snapshot document does not contain input message revisions.");
            }

            if (document.SchemaVersion == CurrentSchemaVersion
                && document.ContextBudget is null)
            {
                throw new InvalidDataException(
                    "Version-two generation snapshot does not contain an explicit context budget.");
            }

            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Generation snapshot file '{filePath}' contains invalid JSON.",
                exception);
        }
    }

    private static SnapshotDocument MapFromSnapshot(GenerationSnapshot snapshot)
    {
        GenerationContextBudget? budget = snapshot.ContextBudget;

        return new SnapshotDocument
        {
            SchemaVersion = budget is null ? LegacySchemaVersion : CurrentSchemaVersion,
            Id = snapshot.Id.Value,
            ConversationId = snapshot.ConversationId.Value,
            TriggeringUserMessageId = snapshot.TriggeringUserMessageId.Value,
            TriggeringUserMessageRevisionId = snapshot.TriggeringUserMessageRevisionId.Value,
            InputMessageRevisionIds = snapshot.InputMessageRevisionIds
                .Select(revisionId => revisionId.Value)
                .ToList(),
            CapturedAtUtc = snapshot.CapturedAtUtc,
            ProviderId = snapshot.ProviderId,
            ModelReference = snapshot.ModelReference,
            ContextSize = snapshot.ContextSize,
            Generation = new GenerationOptionsDocument
            {
                MaxOutputTokens = snapshot.GenerationOptions.MaxOutputTokens,
                Temperature = snapshot.GenerationOptions.Temperature,
                TopP = snapshot.GenerationOptions.TopP,
                TopK = snapshot.GenerationOptions.TopK,
                Seed = snapshot.GenerationOptions.Seed,
                ReasoningEnabled = snapshot.GenerationOptions.ReasoningEnabled,
                ReasoningBudgetTokens = snapshot.GenerationOptions.ReasoningBudgetTokens,
            },
            ProfileId = snapshot.ProfileId?.Value,
            ProfileRevisionId = snapshot.ProfileRevisionId?.Value,
            ContextRevisionId = snapshot.ContextRevisionId?.Value,
            ContextBudget = budget is null
                ? null
                : new ContextBudgetDocument
                {
                    ContextWindowTokens = budget.ContextWindowTokens,
                    ReservedOutputTokens = budget.ReservedOutputTokens,
                    MaximumInputTokens = budget.MaximumInputTokens,
                },
            PayloadHash = snapshot.PayloadHash,
        };
    }

    private static GenerationSnapshot MapToSnapshot(SnapshotDocument document)
    {
        try
        {
            GenerationOptionsDocument generation = document.Generation
                ?? throw new InvalidDataException(
                    "Generation snapshot document does not contain generation options.");
            List<Guid> inputRevisionIds = document.InputMessageRevisionIds
                ?? throw new InvalidDataException(
                    "Generation snapshot document does not contain input message revisions.");
            InferenceGenerationOptions options = new(
                generation.MaxOutputTokens,
                generation.Temperature,
                generation.TopP,
                generation.TopK,
                generation.Seed,
                generation.ReasoningEnabled,
                generation.ReasoningBudgetTokens);
            InferenceProviderConfiguration providerConfiguration = new(
                document.ProviderId,
                document.ModelReference,
                document.ContextSize,
                options);

            GenerationSnapshot snapshot;
            if (document.SchemaVersion == LegacySchemaVersion)
            {
                snapshot = new GenerationSnapshot(
                    new GenerationSnapshotId(document.Id),
                    new ConversationId(document.ConversationId),
                    new MessageId(document.TriggeringUserMessageId),
                    new MessageRevisionId(document.TriggeringUserMessageRevisionId),
                    inputRevisionIds.Select(value => new MessageRevisionId(value)),
                    document.CapturedAtUtc,
                    providerConfiguration,
                    document.ProfileId is Guid profileId
                        ? new GenerationProfileId(profileId)
                        : null,
                    document.ProfileRevisionId is Guid profileRevisionId
                        ? new GenerationProfileRevisionId(profileRevisionId)
                        : null);
            }
            else
            {
                ContextBudgetDocument persistedBudget = document.ContextBudget
                    ?? throw new InvalidDataException(
                        "Version-two generation snapshot does not contain an explicit context budget.");
                GenerationContextBudget budget = new(
                    persistedBudget.ContextWindowTokens,
                    persistedBudget.ReservedOutputTokens);

                if (budget.MaximumInputTokens != persistedBudget.MaximumInputTokens)
                {
                    throw new InvalidDataException(
                        "Stored generation snapshot context budget has an inconsistent maximum-input value.");
                }

                snapshot = new GenerationSnapshot(
                    new GenerationSnapshotId(document.Id),
                    new ConversationId(document.ConversationId),
                    new MessageId(document.TriggeringUserMessageId),
                    new MessageRevisionId(document.TriggeringUserMessageRevisionId),
                    inputRevisionIds.Select(value => new MessageRevisionId(value)),
                    document.CapturedAtUtc,
                    providerConfiguration,
                    document.ProfileId is Guid profileId
                        ? new GenerationProfileId(profileId)
                        : null,
                    document.ProfileRevisionId is Guid profileRevisionId
                        ? new GenerationProfileRevisionId(profileRevisionId)
                        : null,
                    document.ContextRevisionId is Guid contextRevisionId
                        ? new ConversationContextRevisionId(contextRevisionId)
                        : null,
                    budget);
            }

            if (string.IsNullOrWhiteSpace(document.PayloadHash))
            {
                throw new InvalidDataException(
                    $"Stored generation snapshot '{snapshot.Id}' has no payload hash.");
            }

            if (!string.Equals(
                snapshot.PayloadHash,
                document.PayloadHash,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Stored generation snapshot '{snapshot.Id}' has a payload-hash mismatch.");
            }

            return snapshot;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Stored generation snapshot contains invalid generation state.",
                exception);
        }
    }

    private sealed class SnapshotDocument
    {
        public int SchemaVersion { get; init; }

        public Guid Id { get; init; }

        public Guid ConversationId { get; init; }

        public Guid TriggeringUserMessageId { get; init; }

        public Guid TriggeringUserMessageRevisionId { get; init; }

        public List<Guid>? InputMessageRevisionIds { get; init; } = [];

        public DateTimeOffset CapturedAtUtc { get; init; }

        public string ProviderId { get; init; } = string.Empty;

        public string? ModelReference { get; init; }

        public int? ContextSize { get; init; }

        public GenerationOptionsDocument? Generation { get; init; }

        public Guid? ProfileId { get; init; }

        public Guid? ProfileRevisionId { get; init; }

        public Guid? ContextRevisionId { get; init; }

        public ContextBudgetDocument? ContextBudget { get; init; }

        public string? PayloadHash { get; init; }
    }

    private sealed class ContextBudgetDocument
    {
        public int? ContextWindowTokens { get; init; }

        public int? ReservedOutputTokens { get; init; }

        public int? MaximumInputTokens { get; init; }
    }

    private sealed class GenerationOptionsDocument
    {
        public int? MaxOutputTokens { get; init; }

        public double? Temperature { get; init; }

        public double? TopP { get; init; }

        public int? TopK { get; init; }

        public int? Seed { get; init; }

        public bool? ReasoningEnabled { get; init; }

        public int? ReasoningBudgetTokens { get; init; }
    }
}

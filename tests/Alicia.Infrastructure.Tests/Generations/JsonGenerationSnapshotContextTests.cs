using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using Alicia.Infrastructure.Generations;

namespace Alicia.Infrastructure.Tests.Generations;

public sealed class JsonGenerationSnapshotContextTests
{
    [Fact]
    public async Task SchemaTwoRoundTripsContextProvenanceAndBudget()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string directory = Path.Combine(
            Path.GetTempPath(),
            "alicia-generation-context-snapshot-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            JsonGenerationSnapshotStore store = new(directory);
            MessageRevisionId triggerRevisionId = MessageRevisionId.New();
            InferenceProviderConfiguration configuration = new(
                "provider.alpha",
                "owner/model",
                contextSize: 8192,
                generation: new InferenceGenerationOptions(maxOutputTokens: 640));
            GenerationContextBudget budget =
                GenerationContextBudget.FromConfiguration(configuration);
            GenerationSnapshot snapshot = new(
                GenerationSnapshotId.New(),
                ConversationId.New(),
                MessageId.New(),
                triggerRevisionId,
                new[] { triggerRevisionId },
                new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero),
                configuration,
                profileId: null,
                profileRevisionId: null,
                ConversationContextRevisionId.New(),
                budget);

            await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(true);
            GenerationSnapshot loaded = Assert.IsType<GenerationSnapshot>(await store
                .FindAsync(snapshot.Id, cancellationToken)
                .ConfigureAwait(true));

            Assert.Equal(snapshot.ContextRevisionId, loaded.ContextRevisionId);
            GenerationContextBudget loadedBudget =
                Assert.IsType<GenerationContextBudget>(loaded.ContextBudget);
            Assert.Equal(8192, loadedBudget.ContextWindowTokens);
            Assert.Equal(640, loadedBudget.ReservedOutputTokens);
            Assert.Equal(7552, loadedBudget.MaximumInputTokens);
            Assert.Equal(snapshot.PayloadHash, loaded.PayloadHash);

            string path = Path.Combine(directory, snapshot.Id.Value.ToString("N") + ".json");
            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(true);
            Assert.Contains("\"schemaVersion\": 2", json, StringComparison.Ordinal);
            Assert.Contains("\"contextRevisionId\"", json, StringComparison.Ordinal);
            Assert.Contains("\"maximumInputTokens\": 7552", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

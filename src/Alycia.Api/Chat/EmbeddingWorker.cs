using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Api.Chat;

internal sealed class EmbeddingWorker(IServiceScopeFactory scopes, ILogger<EmbeddingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var index = scope.ServiceProvider.GetRequiredService<BackgroundEmbeddingIndex>();
                var store = scope.ServiceProvider.GetRequiredService<PostgresEmbeddingStore>();
                await scope.ServiceProvider.GetRequiredService<TemporalMemoryStore>().RefreshAsync(stoppingToken);
                await index.DiscoverAsync(stoppingToken);
                foreach (var job in await store.JobsAsync(stoppingToken)) await index.ProcessAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogWarning("Indexation différée : {ErrorType}", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}

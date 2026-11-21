using Alycia.Modules.Chat.Application;

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
                var maintenance = scope.ServiceProvider.GetRequiredService<IIndexMaintenance>();
                await maintenance.RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogWarning("Indexation différée : {ErrorType}", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}

namespace MadeInMinas.Api.Services;

public sealed class AiChatWorker(IServiceScopeFactory scopes, ILogger<AiChatWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<AiChatCoordinator>();
                var lease = await coordinator.AcquireAsync(stoppingToken);
                if (lease is not null)
                {
                    await coordinator.ProcessAsync(lease, stoppingToken);
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning("AI chat worker could not process a turn ({FailureType}).", exception.GetType().Name);
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

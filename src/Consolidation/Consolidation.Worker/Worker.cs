using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BancoCarrefour.Consolidation.Worker;

internal sealed class Worker(
    RabbitMqEntryCreatedConsumer consumer,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            consumer.Start(stoppingToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao executar consumer de consolidação.");
            throw;
        }
    }
}

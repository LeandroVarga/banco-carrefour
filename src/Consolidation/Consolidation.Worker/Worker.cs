using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BancoCarrefour.Consolidation.Worker.Sqs;

namespace BancoCarrefour.Consolidation.Worker;

internal sealed class Worker(
    SqsFinancialEntryConsumer consumer,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await consumer.PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha ao executar ciclo de consumo SQS.");
            }
        }
    }
}

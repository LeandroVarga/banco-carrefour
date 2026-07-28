using BancoCarrefour.Ledger.Application.PublishOutbox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BancoCarrefour.Ledger.OutboxPublisher;

internal sealed class Worker(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxPublisherOptions> options,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var useCase = scope.ServiceProvider.GetRequiredService<IPublishPendingEventsUseCase>();

                var result = await useCase.PublishAsync(stoppingToken);
                if (result.Claimed > 0)
                {
                    logger.LogInformation(
                        "Ciclo de publicação da Outbox concluído. Claimed={Claimed}; Published={Published}; Failed={Failed}",
                        result.Claimed,
                        result.Published,
                        result.Failed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha ao executar ciclo de publicação da Outbox.");
            }

            await Task.Delay(options.Value.PollingInterval, stoppingToken);
        }
    }
}

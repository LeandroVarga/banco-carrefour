using Microsoft.Extensions.Hosting;

namespace BancoCarrefour.Ledger.OutboxPublisher;

internal sealed class Worker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}

namespace BancoCarrefour.Ledger.Application.PublishOutbox;

public sealed class PublishPendingEventsUseCase(
    IOutboxStore outboxStore,
    IIntegrationEventPublisher publisher,
    PublisherInstance publisherInstance,
    TimeProvider timeProvider,
    OutboxPublishingOptions options) : IPublishPendingEventsUseCase
{
    private const int LastErrorMaxLength = 2048;

    public async Task<OutboxPublishResult> PublishAsync(CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(1, options.BatchSize);
        var claimed = await outboxStore.ClaimNextBatchAsync(
            batchSize,
            publisherInstance.Id,
            options.ClaimTimeout,
            cancellationToken);

        var published = 0;
        var failed = 0;

        foreach (var outboxEvent in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await publisher.PublishAsync(outboxEvent, cancellationToken);
                await outboxStore.MarkPublishedAsync(
                    outboxEvent.OutboxId,
                    publisherInstance.Id,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                published++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var nextAttemptAt = timeProvider.GetUtcNow() + CalculateRetryDelay(outboxEvent.Attempts);
                await outboxStore.MarkFailedAsync(
                    new OutboxFailure(
                        outboxEvent.OutboxId,
                        Truncate(exception.Message),
                        nextAttemptAt),
                    publisherInstance.Id,
                    cancellationToken);
                failed++;
            }
        }

        return new OutboxPublishResult(claimed.Count, published, failed);
    }

    private TimeSpan CalculateRetryDelay(int attempts)
    {
        var multiplier = Math.Pow(2, Math.Clamp(attempts - 1, 0, 6));
        var delay = TimeSpan.FromMilliseconds(options.BaseRetryDelay.TotalMilliseconds * multiplier);

        return delay <= options.MaxRetryDelay ? delay : options.MaxRetryDelay;
    }

    private static string Truncate(string value)
    {
        return value.Length <= LastErrorMaxLength ? value : value[..LastErrorMaxLength];
    }
}

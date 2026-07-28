namespace BancoCarrefour.Ledger.Application.PublishOutbox;

public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxEvent>> ClaimNextBatchAsync(
        int batchSize,
        string lockedBy,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken);

    Task MarkPublishedAsync(
        Guid outboxId,
        string lockedBy,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        OutboxFailure failure,
        string lockedBy,
        CancellationToken cancellationToken);
}

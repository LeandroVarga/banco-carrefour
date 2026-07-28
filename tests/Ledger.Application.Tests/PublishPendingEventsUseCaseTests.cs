using BancoCarrefour.Ledger.Application.PublishOutbox;
using Xunit;

namespace BancoCarrefour.Ledger.Application.Tests;

public sealed class PublishPendingEventsUseCaseTests
{
    private readonly FixedTimeProvider timeProvider = new(DateTimeOffset.Parse("2026-07-11T13:45:05Z"));

    [Fact]
    public async Task PublishAsync_publica_eventos_reclamados_e_marca_como_published()
    {
        var outboxEvent = CreateEvent();
        var store = new FakeOutboxStore([outboxEvent]);
        var useCase = CreateUseCase(store, new FakePublisher(), "publisher-a");

        var result = await useCase.PublishAsync(CancellationToken.None);

        Assert.Equal(1, result.Claimed);
        Assert.Equal(1, result.Published);
        Assert.Equal(0, result.Failed);
        Assert.Equal(outboxEvent.OutboxId, store.PublishedOutboxId);
        Assert.Equal("publisher-a", store.LockedBy);
        Assert.Equal(timeProvider.GetUtcNow(), store.PublishedAt);
    }

    [Fact]
    public async Task PublishAsync_falha_de_publicacao_marca_retry()
    {
        var outboxEvent = CreateEvent(attempts: 3);
        var store = new FakeOutboxStore([outboxEvent]);
        var useCase = CreateUseCase(store, new FakePublisher(new InvalidOperationException("falha sqs")), "publisher-a");

        var result = await useCase.PublishAsync(CancellationToken.None);

        Assert.Equal(1, result.Claimed);
        Assert.Equal(0, result.Published);
        Assert.Equal(1, result.Failed);
        Assert.NotNull(store.Failure);
        Assert.Equal(outboxEvent.OutboxId, store.Failure.OutboxId);
        Assert.Equal("falha sqs", store.Failure.Error);
        Assert.Equal(timeProvider.GetUtcNow() + TimeSpan.FromSeconds(4), store.Failure.NextAttemptAt);
    }

    [Fact]
    public async Task PublishAsync_usa_batchSize_minimo_um()
    {
        var store = new FakeOutboxStore([]);
        var useCase = CreateUseCase(store, new FakePublisher(), "publisher-a", batchSize: 0);

        await useCase.PublishAsync(CancellationToken.None);

        Assert.Equal(1, store.BatchSize);
    }

    private PublishPendingEventsUseCase CreateUseCase(
        FakeOutboxStore store,
        IIntegrationEventPublisher publisher,
        string publisherId,
        int batchSize = 10)
    {
        return new PublishPendingEventsUseCase(
            store,
            publisher,
            new PublisherInstance(publisherId),
            timeProvider,
            new OutboxPublishingOptions
            {
                BatchSize = batchSize,
                ClaimTimeout = TimeSpan.FromMinutes(2),
                BaseRetryDelay = TimeSpan.FromSeconds(1),
                MaxRetryDelay = TimeSpan.FromSeconds(10)
            });
    }

    private static OutboxEvent CreateEvent(int attempts = 1)
    {
        return new OutboxEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "FinancialEntryRegistered",
            1,
            "{}",
            attempts,
            DateTimeOffset.Parse("2026-07-11T13:45:00Z"));
    }

    private sealed class FakeOutboxStore(IReadOnlyList<OutboxEvent> events) : IOutboxStore
    {
        public int BatchSize { get; private set; }

        public string? LockedBy { get; private set; }

        public Guid? PublishedOutboxId { get; private set; }

        public DateTimeOffset? PublishedAt { get; private set; }

        public OutboxFailure? Failure { get; private set; }

        public Task<IReadOnlyList<OutboxEvent>> ClaimNextBatchAsync(
            int batchSize,
            string lockedBy,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            BatchSize = batchSize;
            LockedBy = lockedBy;

            return Task.FromResult(events);
        }

        public Task MarkPublishedAsync(
            Guid outboxId,
            string lockedBy,
            DateTimeOffset publishedAt,
            CancellationToken cancellationToken)
        {
            PublishedOutboxId = outboxId;
            LockedBy = lockedBy;
            PublishedAt = publishedAt;

            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            OutboxFailure failure,
            string lockedBy,
            CancellationToken cancellationToken)
        {
            Failure = failure;
            LockedBy = lockedBy;

            return Task.CompletedTask;
        }
    }

    private sealed class FakePublisher(Exception? exception = null) : IIntegrationEventPublisher
    {
        public Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}

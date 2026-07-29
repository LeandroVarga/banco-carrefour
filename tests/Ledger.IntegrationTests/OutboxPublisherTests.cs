using BancoCarrefour.Ledger.Application.PublishOutbox;
using BancoCarrefour.Ledger.Infrastructure;
using BancoCarrefour.Ledger.Infrastructure.Entities;
using BancoCarrefour.Ledger.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BancoCarrefour.Ledger.IntegrationTests;

[Collection(LedgerIntegrationCollection.Name)]
public sealed class OutboxPublisherTests : IAsyncLifetime
{
    private readonly LedgerIntegrationTestFixture fixture;
    private readonly LedgerApiFactory factory;

    public OutboxPublisherTests(LedgerIntegrationTestFixture fixture)
    {
        this.fixture = fixture;
        factory = new LedgerApiFactory(fixture.ConnectionString);
    }

    public async Task InitializeAsync()
    {
        await fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        factory.Dispose();

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ClaimNextBatch_duas_instancias_nao_recebem_mesma_mensagem()
    {
        var firstEventId = Guid.NewGuid();
        var secondEventId = Guid.NewGuid();
        await InsertOutboxMessageAsync(firstEventId);
        await InsertOutboxMessageAsync(secondEventId);

        var first = ClaimWithNewContextAsync("publisher-a", batchSize: 1);
        var second = ClaimWithNewContextAsync("publisher-b", batchSize: 1);

        var claimed = (await Task.WhenAll(first, second)).SelectMany(events => events).ToArray();

        Assert.Equal(2, claimed.Length);
        Assert.Equal(2, claimed.Select(x => x.EventId).Distinct().Count());
    }

    [Fact]
    public async Task ClaimNextBatch_claim_expirado_volta_a_ser_elegivel()
    {
        var eventId = Guid.NewGuid();
        await InsertOutboxMessageAsync(
            eventId,
            OutboxMessageStatus.Processing,
            lockedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            lockedBy: "publisher-morto");

        var claimed = await ClaimWithNewContextAsync("publisher-novo", batchSize: 1);

        Assert.Single(claimed);
        Assert.Equal(eventId, claimed[0].EventId);
    }

    [Fact]
    public async Task ClaimNextBatch_nao_reclama_publicada_ou_nextAttemptAt_futuro()
    {
        await InsertOutboxMessageAsync(Guid.NewGuid(), OutboxMessageStatus.Published);
        await InsertOutboxMessageAsync(
            Guid.NewGuid(),
            OutboxMessageStatus.Pending,
            nextAttemptAt: DateTimeOffset.UtcNow.AddMinutes(5));

        var claimed = await ClaimWithNewContextAsync("publisher-a", batchSize: 10);

        Assert.Empty(claimed);
    }

    [Fact]
    public async Task PublishAsync_publicacao_com_sucesso_marca_outbox_como_published()
    {
        var eventId = Guid.NewGuid();
        await InsertOutboxMessageAsync(eventId);
        var useCase = CreateUseCase(new FakePublisher());

        var result = await useCase.PublishAsync(CancellationToken.None);
        var outbox = await GetOutboxMessageAsync(eventId);

        Assert.Equal(1, result.Claimed);
        Assert.Equal(1, result.Published);
        Assert.Equal(0, result.Failed);
        Assert.Equal(OutboxMessageStatus.Published, outbox.Status);
        Assert.NotNull(outbox.PublishedAt);
        Assert.Null(outbox.LockedAt);
        Assert.Null(outbox.LockedBy);
    }

    [Fact]
    public async Task PublishAsync_falha_de_publicacao_libera_claim_e_agenda_retry()
    {
        var eventId = Guid.NewGuid();
        await InsertOutboxMessageAsync(eventId);
        var useCase = CreateUseCase(new FakePublisher(new InvalidOperationException("falha sqs")));

        var result = await useCase.PublishAsync(CancellationToken.None);
        var outbox = await GetOutboxMessageAsync(eventId);

        Assert.Equal(1, result.Claimed);
        Assert.Equal(0, result.Published);
        Assert.Equal(1, result.Failed);
        Assert.Equal(OutboxMessageStatus.Pending, outbox.Status);
        Assert.Equal(1, outbox.Attempts);
        Assert.NotNull(outbox.NextAttemptAt);
        Assert.Null(outbox.LockedAt);
        Assert.Null(outbox.LockedBy);
        Assert.Contains("falha sqs", outbox.LastError);
    }

    private IPublishPendingEventsUseCase CreateUseCase(IIntegrationEventPublisher publisher)
    {
        var dbContext = CreateContext();
        return new PublishPendingEventsUseCase(
            new PostgresOutboxStore(dbContext, TimeProvider.System),
            publisher,
            new PublisherInstance(),
            TimeProvider.System,
            new OutboxPublishingOptions
            {
                BatchSize = 10,
                ClaimTimeout = TimeSpan.FromMinutes(2),
                BaseRetryDelay = TimeSpan.FromMilliseconds(100),
                MaxRetryDelay = TimeSpan.FromSeconds(1)
            });
    }

    private async Task<IReadOnlyList<OutboxEvent>> ClaimWithNewContextAsync(string lockedBy, int batchSize)
    {
        await using var dbContext = CreateContext();
        var store = new PostgresOutboxStore(dbContext, TimeProvider.System);

        return await store.ClaimNextBatchAsync(batchSize, lockedBy, TimeSpan.FromMinutes(2), CancellationToken.None);
    }

    private async Task InsertOutboxMessageAsync(
        Guid eventId,
        OutboxMessageStatus status = OutboxMessageStatus.Pending,
        DateTimeOffset? lockedAt = null,
        string? lockedBy = null,
        DateTimeOffset? nextAttemptAt = null)
    {
        await using var dbContext = CreateContext();

        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            OutboxId = Guid.NewGuid(),
            EventId = eventId,
            EventType = "FinancialEntryRegistered",
            EventVersion = 1,
            Payload = $$"""
                {
                  "eventId": "{{eventId}}",
                  "entryId": "{{Guid.NewGuid()}}",
                  "eventType": "FinancialEntryRegistered",
                  "eventVersion": 1,
                  "occurredAt": "2026-07-11T13:45:00Z",
                  "registeredAt": "2026-07-11T13:45:05Z",
                  "correlationId": "corr-outbox",
                  "merchantId": "merchant-001",
                  "businessDate": "2026-07-11",
                  "type": "CREDIT",
                  "amount": "150.75",
                  "currency": "BRL",
                  "description": "Venda cartão"
                }
                """,
            Status = status,
            OccurredAt = DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            CreatedAt = DateTimeOffset.UtcNow,
            Attempts = 0,
            LockedAt = lockedAt,
            LockedBy = lockedBy,
            NextAttemptAt = nextAttemptAt
        });

        await dbContext.SaveChangesAsync();
    }

    private async Task<OutboxMessage> GetOutboxMessageAsync(Guid eventId)
    {
        await using var dbContext = CreateContext();

        return await dbContext.OutboxMessages.AsNoTracking().SingleAsync(message => message.EventId == eventId);
    }

    private LedgerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseNpgsql(factory.ConnectionString)
            .Options;

        return new LedgerDbContext(options);
    }

    private sealed class FakePublisher(Exception? exception = null) : IIntegrationEventPublisher
    {
        public Task PublishAsync(
            OutboxEvent outboxEvent,
            CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return Task.CompletedTask;
        }
    }
}

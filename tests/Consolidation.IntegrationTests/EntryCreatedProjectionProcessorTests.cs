using BancoCarrefour.Consolidation.Application;
using BancoCarrefour.Consolidation.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BancoCarrefour.Consolidation.IntegrationTests;

public sealed class EntryCreatedProjectionProcessorTests : IAsyncLifetime
{
    private static readonly string ConnectionString = Environment.GetEnvironmentVariable("CONSOLIDATION_TEST_CONNECTION_STRING")
        ?? "Host=consolidation-postgres;Port=5432;Database=consolidation;Username=consolidation;Password=consolidation";

    private readonly FixedTimeProvider timeProvider = new(DateTimeOffset.Parse("2026-07-11T14:00:00Z"));

    public async Task InitializeAsync()
    {
        await ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Processar_credit_cria_dailyBalance_com_totais_corretos()
    {
        await using var context = CreateContext();
        var processor = CreateProcessor(context);

        var result = await processor.ProcessAsync(CreateEvent(type: "CREDIT", amount: "150.75"), CancellationToken.None);

        var balance = await context.DailyBalances.AsNoTracking().SingleAsync();
        Assert.True(result.Applied);
        Assert.False(result.Duplicate);
        Assert.Equal(balance.DailyBalanceId, result.DailyBalanceId);
        Assert.Equal(150.75m, balance.TotalCredits);
        Assert.Equal(0m, balance.TotalDebits);
        Assert.Equal(150.75m, balance.Balance);
        Assert.Equal(1, balance.EntryCount);
        Assert.Equal("BRL", balance.Currency);
    }

    [Fact]
    public async Task Processar_debit_cria_dailyBalance_com_saldo_negativo()
    {
        await using var context = CreateContext();
        var processor = CreateProcessor(context);

        await processor.ProcessAsync(CreateEvent(type: "DEBIT", amount: "25.10"), CancellationToken.None);

        var balance = await context.DailyBalances.AsNoTracking().SingleAsync();
        Assert.Equal(0m, balance.TotalCredits);
        Assert.Equal(25.10m, balance.TotalDebits);
        Assert.Equal(-25.10m, balance.Balance);
        Assert.Equal(1, balance.EntryCount);
    }

    [Fact]
    public async Task Processar_credit_e_debit_para_mesmo_merchant_data_atualiza_mesmo_dailyBalance()
    {
        await using var context = CreateContext();
        var processor = CreateProcessor(context);

        await processor.ProcessAsync(CreateEvent(eventId: Guid.NewGuid(), type: "CREDIT", amount: "150.75"), CancellationToken.None);
        await processor.ProcessAsync(CreateEvent(eventId: Guid.NewGuid(), type: "DEBIT", amount: "25.10"), CancellationToken.None);

        var balance = await context.DailyBalances.AsNoTracking().SingleAsync();
        Assert.Equal(150.75m, balance.TotalCredits);
        Assert.Equal(25.10m, balance.TotalDebits);
        Assert.Equal(125.65m, balance.Balance);
        Assert.Equal(2, balance.EntryCount);
        Assert.Equal(2, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Processar_mesmo_eventId_duas_vezes_nao_duplica_saldo_nem_entryCount()
    {
        await using var context = CreateContext();
        var processor = CreateProcessor(context);
        var eventId = Guid.NewGuid();

        var first = await processor.ProcessAsync(CreateEvent(eventId: eventId, amount: "150.75"), CancellationToken.None);
        var duplicate = await processor.ProcessAsync(CreateEvent(eventId: eventId, amount: "150.75"), CancellationToken.None);

        var balance = await context.DailyBalances.AsNoTracking().SingleAsync();
        Assert.True(first.Applied);
        Assert.False(first.Duplicate);
        Assert.False(duplicate.Applied);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(150.75m, balance.TotalCredits);
        Assert.Equal(150.75m, balance.Balance);
        Assert.Equal(1, balance.EntryCount);
        Assert.Equal(1, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Eventos_distintos_concorrentes_para_mesmo_merchant_data_nao_perdem_atualizacao()
    {
        var events = Enumerable.Range(0, 10)
            .Select(index => CreateEvent(
                eventId: Guid.NewGuid(),
                type: "CREDIT",
                amount: "10.00",
                occurredAt: DateTimeOffset.Parse("2026-07-11T13:00:00Z").AddMinutes(index)))
            .Concat(Enumerable.Range(0, 5)
                .Select(index => CreateEvent(
                    eventId: Guid.NewGuid(),
                    type: "DEBIT",
                    amount: "3.00",
                    occurredAt: DateTimeOffset.Parse("2026-07-11T14:00:00Z").AddMinutes(index))))
            .ToArray();

        var results = await Task.WhenAll(events.Select(ProcessWithNewContextAsync));

        await using var context = CreateContext();
        var balance = await context.DailyBalances.AsNoTracking().SingleAsync();

        Assert.All(results, result =>
        {
            Assert.True(result.Applied);
            Assert.False(result.Duplicate);
        });
        Assert.Equal(100.00m, balance.TotalCredits);
        Assert.Equal(15.00m, balance.TotalDebits);
        Assert.Equal(85.00m, balance.Balance);
        Assert.Equal(15, balance.EntryCount);
        Assert.Equal(DateTimeOffset.Parse("2026-07-11T14:04:00Z"), balance.LastEventOccurredAt);
        Assert.Equal(15, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Evento_duplicado_concorrente_nao_duplica_saldo_nem_entryCount()
    {
        var duplicatedEvent = CreateEvent(eventId: Guid.NewGuid(), amount: "150.75");
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => ProcessWithNewContextAsync(duplicatedEvent));

        var results = await Task.WhenAll(tasks);

        await using var context = CreateContext();
        var balance = await context.DailyBalances.AsNoTracking().SingleAsync();

        Assert.Equal(1, results.Count(result => result.Applied));
        Assert.Equal(7, results.Count(result => result.Duplicate));
        Assert.Equal(150.75m, balance.TotalCredits);
        Assert.Equal(0m, balance.TotalDebits);
        Assert.Equal(150.75m, balance.Balance);
        Assert.Equal(1, balance.EntryCount);
        Assert.Equal(1, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Eventos_de_merchants_diferentes_na_mesma_data_nao_colidem()
    {
        await using var context = CreateContext();
        var processor = CreateProcessor(context);

        await processor.ProcessAsync(CreateEvent(eventId: Guid.NewGuid(), merchantId: "merchant-001", amount: "150.75"), CancellationToken.None);
        await processor.ProcessAsync(CreateEvent(eventId: Guid.NewGuid(), merchantId: "merchant-002", amount: "25.10"), CancellationToken.None);

        var balances = await context.DailyBalances.AsNoTracking().OrderBy(x => x.MerchantId).ToListAsync();
        Assert.Equal(2, balances.Count);
        Assert.Equal("merchant-001", balances[0].MerchantId);
        Assert.Equal(150.75m, balances[0].Balance);
        Assert.Equal("merchant-002", balances[1].MerchantId);
        Assert.Equal(25.10m, balances[1].Balance);
    }

    [Fact]
    public async Task Eventos_de_datas_diferentes_criam_dailyBalances_separados()
    {
        await using var context = CreateContext();
        var processor = CreateProcessor(context);

        await processor.ProcessAsync(CreateEvent(eventId: Guid.NewGuid(), businessDate: "2026-07-11"), CancellationToken.None);
        await processor.ProcessAsync(CreateEvent(eventId: Guid.NewGuid(), businessDate: "2026-07-12"), CancellationToken.None);

        var balances = await context.DailyBalances.AsNoTracking().OrderBy(x => x.BusinessDate).ToListAsync();
        Assert.Equal(2, balances.Count);
        Assert.Equal(new DateOnly(2026, 7, 11), balances[0].BusinessDate);
        Assert.Equal(new DateOnly(2026, 7, 12), balances[1].BusinessDate);
    }

    [Fact]
    public async Task LastEventOccurredAt_mantem_maior_occurredAt()
    {
        await using var context = CreateContext();
        var processor = CreateProcessor(context);

        await processor.ProcessAsync(CreateEvent(eventId: Guid.NewGuid(), occurredAt: DateTimeOffset.Parse("2026-07-11T13:45:00Z")), CancellationToken.None);
        await processor.ProcessAsync(CreateEvent(eventId: Guid.NewGuid(), occurredAt: DateTimeOffset.Parse("2026-07-11T13:30:00Z")), CancellationToken.None);

        var balance = await context.DailyBalances.AsNoTracking().SingleAsync();
        Assert.Equal(DateTimeOffset.Parse("2026-07-11T13:45:00Z"), balance.LastEventOccurredAt);
        Assert.Equal(timeProvider.GetUtcNow(), balance.LastUpdatedAt);
    }

    [Fact]
    public async Task Payload_invalido_nao_persiste_dailyBalance_nem_processedEvent()
    {
        await using var context = CreateContext();
        var processor = CreateProcessor(context);

        await Assert.ThrowsAsync<ProjectionValidationException>(() =>
            processor.ProcessAsync(CreateEvent(type: "INVALID"), CancellationToken.None));

        Assert.Equal(0, await context.DailyBalances.CountAsync());
        Assert.Equal(0, await context.ProcessedEvents.CountAsync());
    }

    private EntryCreatedProjectionProcessor CreateProcessor(ConsolidationDbContext context)
    {
        return new EntryCreatedProjectionProcessor(context, timeProvider);
    }

    private async Task<ProjectionResult> ProcessWithNewContextAsync(EntryCreatedEvent message)
    {
        await using var context = CreateContext();
        var processor = CreateProcessor(context);

        return await processor.ProcessAsync(message, CancellationToken.None);
    }

    private static async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();

        await context.Database.MigrateAsync();
        await context.ProcessedEvents.ExecuteDeleteAsync();
        await context.DailyBalances.ExecuteDeleteAsync();
    }

    private static ConsolidationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ConsolidationDbContext(options);
    }

    private static EntryCreatedEvent CreateEvent(
        Guid? eventId = null,
        string eventType = "EntryCreated",
        int eventVersion = 1,
        DateTimeOffset? occurredAt = null,
        string merchantId = "merchant-001",
        string businessDate = "2026-07-11",
        string type = "CREDIT",
        string amount = "150.75",
        string currency = "BRL")
    {
        return new EntryCreatedEvent(
            eventId ?? Guid.NewGuid(),
            eventType,
            eventVersion,
            occurredAt ?? DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            DateTimeOffset.Parse("2026-07-11T13:45:05Z"),
            "corr-test",
            Guid.NewGuid(),
            merchantId,
            businessDate,
            type,
            amount,
            currency,
            "Venda cartão");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}

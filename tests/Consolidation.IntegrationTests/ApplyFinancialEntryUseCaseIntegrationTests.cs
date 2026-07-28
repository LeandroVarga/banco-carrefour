using BancoCarrefour.Consolidation.Application;
using BancoCarrefour.Consolidation.Application.ApplyFinancialEntry;
using BancoCarrefour.Consolidation.Infrastructure;
using BancoCarrefour.Consolidation.Infrastructure.DailyBalances;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BancoCarrefour.Consolidation.IntegrationTests;

[Collection(ConsolidationIntegrationCollection.Name)]
public sealed class ApplyFinancialEntryUseCaseIntegrationTests : IAsyncLifetime
{
    private readonly ConsolidationIntegrationTestFixture fixture;
    private readonly FixedTimeProvider timeProvider = new(DateTimeOffset.Parse("2026-07-11T14:00:00Z"));

    public ApplyFinancialEntryUseCaseIntegrationTests(ConsolidationIntegrationTestFixture fixture)
    {
        this.fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await fixture.ResetConsolidationDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Apply_credit_cria_dailyBalance_com_totais_corretos()
    {
        await using var context = CreateContext();
        var useCase = CreateUseCase(context);

        var result = await useCase.ApplyAsync(CreateCommand(type: "CREDIT", amount: "150.75"), CancellationToken.None);

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
    public async Task Apply_financialEntryRegistered_cria_dailyBalance()
    {
        await using var context = CreateContext();
        var useCase = CreateUseCase(context);

        var result = await useCase.ApplyAsync(CreateCommand(eventType: "FinancialEntryRegistered"), CancellationToken.None);

        var balance = await context.DailyBalances.AsNoTracking().SingleAsync();
        var processed = await context.ProcessedEvents.AsNoTracking().SingleAsync();

        Assert.True(result.Applied);
        Assert.Equal("FinancialEntryRegistered", processed.EventType);
        Assert.Equal(150.75m, balance.TotalCredits);
        Assert.Equal(150.75m, balance.Balance);
    }

    [Fact]
    public async Task Apply_debit_cria_dailyBalance_com_saldo_negativo()
    {
        await using var context = CreateContext();
        var useCase = CreateUseCase(context);

        await useCase.ApplyAsync(CreateCommand(type: "DEBIT", amount: "25.10"), CancellationToken.None);

        var balance = await context.DailyBalances.AsNoTracking().SingleAsync();
        Assert.Equal(0m, balance.TotalCredits);
        Assert.Equal(25.10m, balance.TotalDebits);
        Assert.Equal(-25.10m, balance.Balance);
        Assert.Equal(1, balance.EntryCount);
    }

    [Fact]
    public async Task Mesmo_eventId_duas_vezes_nao_duplica_saldo()
    {
        await using var context = CreateContext();
        var useCase = CreateUseCase(context);
        var eventId = Guid.NewGuid();

        var first = await useCase.ApplyAsync(CreateCommand(eventId: eventId), CancellationToken.None);
        var duplicate = await useCase.ApplyAsync(CreateCommand(eventId: eventId), CancellationToken.None);

        var balance = await context.DailyBalances.AsNoTracking().SingleAsync();
        Assert.True(first.Applied);
        Assert.False(first.Duplicate);
        Assert.False(duplicate.Applied);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(150.75m, balance.TotalCredits);
        Assert.Equal(1, balance.EntryCount);
        Assert.Equal(1, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Eventos_distintos_concorrentes_para_mesmo_merchant_data_nao_perdem_atualizacao()
    {
        var commands = Enumerable.Range(0, 10)
            .Select(index => CreateCommand(
                eventId: Guid.NewGuid(),
                type: "CREDIT",
                amount: "10.00",
                occurredAt: DateTimeOffset.Parse("2026-07-11T13:00:00Z").AddMinutes(index)))
            .Concat(Enumerable.Range(0, 5)
                .Select(index => CreateCommand(
                    eventId: Guid.NewGuid(),
                    type: "DEBIT",
                    amount: "3.00",
                    occurredAt: DateTimeOffset.Parse("2026-07-11T14:00:00Z").AddMinutes(index))))
            .ToArray();

        var results = await Task.WhenAll(commands.Select(ApplyWithNewContextAsync));

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
    }

    [Fact]
    public async Task Payload_invalido_nao_persiste_dailyBalance_nem_processedEvent()
    {
        await using var context = CreateContext();
        var useCase = CreateUseCase(context);

        await Assert.ThrowsAsync<ProjectionValidationException>(() =>
            useCase.ApplyAsync(CreateCommand(type: "INVALID"), CancellationToken.None));

        Assert.Equal(0, await context.DailyBalances.CountAsync());
        Assert.Equal(0, await context.ProcessedEvents.CountAsync());
    }

    private IApplyFinancialEntryUseCase CreateUseCase(ConsolidationDbContext context)
    {
        return new ApplyFinancialEntryUseCase(
            new EfDailyBalanceProjectionStore(context),
            timeProvider);
    }

    private async Task<ProjectionResult> ApplyWithNewContextAsync(ApplyFinancialEntryCommand command)
    {
        await using var context = CreateContext();
        var useCase = CreateUseCase(context);

        return await useCase.ApplyAsync(command, CancellationToken.None);
    }

    private ConsolidationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(fixture.ConsolidationConnectionString)
            .Options;

        return new ConsolidationDbContext(options);
    }

    private static ApplyFinancialEntryCommand CreateCommand(
        Guid? eventId = null,
        string eventType = "EntryCreated",
        int eventVersion = 1,
        DateTimeOffset? occurredAt = null,
        string merchantId = "merchant-001",
        string businessDate = "2026-07-11",
        Guid? entryId = null,
        string type = "CREDIT",
        string amount = "150.75",
        string currency = "BRL")
    {
        return new ApplyFinancialEntryCommand(
            eventId ?? Guid.NewGuid(),
            eventType,
            eventVersion,
            occurredAt ?? DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            DateTimeOffset.Parse("2026-07-11T13:45:05Z"),
            "corr-test",
            entryId ?? Guid.NewGuid(),
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

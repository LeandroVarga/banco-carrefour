using BancoCarrefour.Ledger.Application.RegisterFinancialEntry;
using BancoCarrefour.Ledger.Infrastructure;
using BancoCarrefour.Ledger.Infrastructure.FinancialEntries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace BancoCarrefour.Ledger.IntegrationTests;

[Collection(LedgerIntegrationCollection.Name)]
public sealed class FinancialEntryRegistrationStoreTests : IAsyncLifetime
{
    private readonly LedgerIntegrationTestFixture fixture;
    private readonly LedgerApiFactory factory;

    public FinancialEntryRegistrationStoreTests(LedgerIntegrationTestFixture fixture)
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
    public async Task Falha_antes_do_commit_nao_deixa_entry_inputIdempotency_ou_outbox_parcial()
    {
        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseNpgsql(factory.ConnectionString)
            .AddInterceptors(new ThrowBeforeCommitInterceptor())
            .Options;

        await using var dbContext = new LedgerDbContext(options);
        var useCase = new RegisterFinancialEntryUseCase(
            new EfFinancialEntryRegistrationStore(dbContext),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-07-11T13:45:05Z")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.RegisterAsync(CreateCommand(), CancellationToken.None));

        await using var verificationContext = CreateContext();
        Assert.Equal(0, await verificationContext.Entries.CountAsync());
        Assert.Equal(0, await verificationContext.InputIdempotencyRecords.CountAsync());
        Assert.Equal(0, await verificationContext.OutboxMessages.CountAsync());
    }

    private LedgerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseNpgsql(factory.ConnectionString)
            .Options;

        return new LedgerDbContext(options);
    }

    private static RegisterFinancialEntryCommand CreateCommand()
    {
        return new RegisterFinancialEntryCommand(
            "merchant-001",
            "idem-fault-001",
            "CREDIT",
            "150.75",
            "BRL",
            DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            "Venda cartão",
            "corr-fault");
    }

    private sealed class ThrowBeforeCommitInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Falha simulada antes do commit.");
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

using BancoCarrefour.Consolidation.Application.ApplyFinancialEntry;
using BancoCarrefour.Consolidation.Domain;
using Xunit;

namespace BancoCarrefour.Consolidation.Application.Tests;

public sealed class ApplyFinancialEntryUseCaseTests
{
    private readonly FixedTimeProvider timeProvider = new(DateTimeOffset.Parse("2026-07-11T13:45:05Z"));

    [Fact]
    public async Task ApplyAsync_valido_chama_store_com_evento_processado_e_contribuicao()
    {
        var store = new FakeProjectionStore(new ProjectionResult(true, false, Guid.NewGuid(), DateOnly.Parse("2026-07-11")));
        var useCase = new ApplyFinancialEntryUseCase(store, timeProvider);

        var result = await useCase.ApplyAsync(CreateCommand(), CancellationToken.None);

        Assert.True(result.Applied);
        Assert.NotNull(store.ProcessedEntry);
        Assert.NotNull(store.Contribution);
        Assert.Equal("merchant-001", store.Contribution.MerchantId.Value);
        Assert.Equal(150.75m, store.Contribution.CreditAmount);
        Assert.Equal(timeProvider.GetUtcNow(), store.ProcessedEntry.ProcessedAt);
    }

    [Fact]
    public async Task ApplyAsync_tipo_invalido_retorna_erro_de_validacao_da_application()
    {
        var useCase = new ApplyFinancialEntryUseCase(
            new FakeProjectionStore(new ProjectionResult(true, false, Guid.NewGuid(), DateOnly.Parse("2026-07-11"))),
            timeProvider);

        var exception = await Assert.ThrowsAsync<ProjectionValidationException>(() =>
            useCase.ApplyAsync(CreateCommand() with { Type = "PIX" }, CancellationToken.None));

        Assert.Contains("type", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_propagada_cancellationToken_para_store()
    {
        var store = new FakeProjectionStore(new ProjectionResult(true, false, Guid.NewGuid(), DateOnly.Parse("2026-07-11")));
        using var cts = new CancellationTokenSource();

        await new ApplyFinancialEntryUseCase(store, timeProvider).ApplyAsync(CreateCommand(), cts.Token);

        Assert.Equal(cts.Token, store.CancellationToken);
    }

    private static ApplyFinancialEntryCommand CreateCommand()
    {
        return new ApplyFinancialEntryCommand(
            Guid.NewGuid(),
            "FinancialEntryRegistered",
            1,
            DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            DateTimeOffset.Parse("2026-07-11T13:45:05Z"),
            "corr-test",
            Guid.NewGuid(),
            "merchant-001",
            "2026-07-11",
            "CREDIT",
            "150.75",
            "BRL",
            "Venda cartão");
    }

    private sealed class FakeProjectionStore(ProjectionResult result) : IDailyBalanceProjectionStore
    {
        public ProcessedFinancialEntry? ProcessedEntry { get; private set; }

        public DailyBalanceContribution? Contribution { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<ProjectionResult> ApplyAsync(
            ProcessedFinancialEntry processedEntry,
            DailyBalanceContribution contribution,
            CancellationToken cancellationToken)
        {
            ProcessedEntry = processedEntry;
            Contribution = contribution;
            CancellationToken = cancellationToken;

            return Task.FromResult(result);
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

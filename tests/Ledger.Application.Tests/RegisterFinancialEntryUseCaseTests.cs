using BancoCarrefour.Ledger.Application.RegisterFinancialEntry;
using Xunit;

namespace BancoCarrefour.Ledger.Application.Tests;

public sealed class RegisterFinancialEntryUseCaseTests
{
    private readonly FixedTimeProvider timeProvider = new(DateTimeOffset.Parse("2026-07-11T13:45:05Z"));

    [Fact]
    public async Task RegisterAsync_criado_retorna_created_e_cria_intencao_de_evento()
    {
        var store = new FakeStore(RegisterFinancialEntryResultStatus.Created);
        var useCase = new RegisterFinancialEntryUseCase(store, timeProvider);

        var result = await useCase.RegisterAsync(CreateCommand(), CancellationToken.None);

        Assert.Equal(RegisterFinancialEntryResultStatus.Created, result.Status);
        Assert.NotNull(result.Entry);
        Assert.NotNull(store.Registration);
        Assert.Equal("merchant-001", store.Registration.Entry.MerchantId.Value);
        Assert.Equal("FinancialEntryRegistered", store.Registration.IntegrationEvent.EventType);
        Assert.Equal(1, store.Registration.IntegrationEvent.EventVersion);
        Assert.Equal(timeProvider.GetUtcNow(), store.Registration.IntegrationEvent.RegisteredAt);
    }

    [Fact]
    public async Task RegisterAsync_replay_equivalente_retorna_replay()
    {
        var store = new FakeStore(RegisterFinancialEntryResultStatus.Replay);
        var useCase = new RegisterFinancialEntryUseCase(store, timeProvider);

        var result = await useCase.RegisterAsync(CreateCommand(), CancellationToken.None);

        Assert.Equal(RegisterFinancialEntryResultStatus.Replay, result.Status);
        Assert.Equal(store.ReplayEntry.EntryId, result.Entry?.EntryId);
    }

    [Fact]
    public async Task RegisterAsync_conflito_retorna_conflict()
    {
        var store = new FakeStore(RegisterFinancialEntryResultStatus.Conflict);
        var useCase = new RegisterFinancialEntryUseCase(store, timeProvider);

        var result = await useCase.RegisterAsync(CreateCommand(), CancellationToken.None);

        Assert.Equal(RegisterFinancialEntryResultStatus.Conflict, result.Status);
        Assert.Null(result.Entry);
    }

    [Fact]
    public async Task RegisterAsync_fingerprint_e_deterministico()
    {
        var firstStore = new FakeStore(RegisterFinancialEntryResultStatus.Created);
        var secondStore = new FakeStore(RegisterFinancialEntryResultStatus.Created);

        await new RegisterFinancialEntryUseCase(firstStore, timeProvider).RegisterAsync(CreateCommand(), CancellationToken.None);
        await new RegisterFinancialEntryUseCase(secondStore, timeProvider).RegisterAsync(CreateCommand(), CancellationToken.None);

        Assert.Equal(firstStore.Registration?.PayloadFingerprint, secondStore.Registration?.PayloadFingerprint);
    }

    [Fact]
    public async Task RegisterAsync_propagada_cancellationToken_para_porta()
    {
        var store = new FakeStore(RegisterFinancialEntryResultStatus.Created);
        using var cts = new CancellationTokenSource();
        var useCase = new RegisterFinancialEntryUseCase(store, timeProvider);

        await useCase.RegisterAsync(CreateCommand(), cts.Token);

        Assert.Equal(cts.Token, store.CancellationToken);
    }

    [Fact]
    public async Task RegisterAsync_falha_de_persistencia_e_propagada()
    {
        var store = new FakeStore(RegisterFinancialEntryResultStatus.Created)
        {
            Exception = new InvalidOperationException("falha simulada")
        };
        var useCase = new RegisterFinancialEntryUseCase(store, timeProvider);

        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.RegisterAsync(CreateCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task RegisterAsync_rejeita_payload_sem_infraestrutura()
    {
        var useCase = new RegisterFinancialEntryUseCase(new FakeStore(RegisterFinancialEntryResultStatus.Created), timeProvider);
        var command = CreateCommand() with { Amount = "0.00" };

        var exception = await Assert.ThrowsAsync<RegisterFinancialEntryValidationException>(() =>
            useCase.RegisterAsync(command, CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Contains("amount", StringComparison.Ordinal));
    }

    private static RegisterFinancialEntryCommand CreateCommand()
    {
        return new RegisterFinancialEntryCommand(
            "merchant-001",
            "idem-0001",
            "CREDIT",
            "150.75",
            "BRL",
            DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            "Venda cartão",
            "corr-test");
    }

    private sealed class FakeStore(RegisterFinancialEntryResultStatus status) : IFinancialEntryRegistrationStore
    {
        public readonly RegisteredFinancialEntry ReplayEntry = new(
            Guid.NewGuid(),
            "merchant-001",
            "2026-07-11",
            "CREDIT",
            "150.75",
            "BRL",
            DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            DateTimeOffset.Parse("2026-07-11T13:45:05Z"),
            "idem-0001");

        public FinancialEntryRegistration? Registration { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Exception? Exception { get; init; }

        public Task<RegisterFinancialEntryResult> RegisterAsync(
            FinancialEntryRegistration registration,
            CancellationToken cancellationToken)
        {
            Registration = registration;
            CancellationToken = cancellationToken;

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(status switch
            {
                RegisterFinancialEntryResultStatus.Created => RegisterFinancialEntryResult.Created(
                    RegisterFinancialEntryUseCase.ToRegisteredEntry(registration.Entry, registration.IdempotencyKey)),
                RegisterFinancialEntryResultStatus.Replay => RegisterFinancialEntryResult.Replay(ReplayEntry),
                RegisterFinancialEntryResultStatus.Conflict => RegisterFinancialEntryResult.Conflict(),
                _ => throw new InvalidOperationException("Status de teste inválido.")
            });
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

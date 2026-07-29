using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace BancoCarrefour.MigrationRunner.Tests;

/// <summary>
/// Prova real (contra um PostgreSQL real via Testcontainers, nunca um mock)
/// do mecanismo de exclusão mútua explícito. PostgreSQL não garante
/// isso sozinho fora de um lock consultivo, e EF Core 8.0.11 (versão real
/// deste repositório) não tem o locking automático introduzido apenas no
/// EF Core 9.
/// </summary>
[Collection(PostgresLockTestCollection.Name)]
public sealed class PostgresMigrationLockTests(PostgresLockTestFixture fixture)
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShortPoll = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Primeiro_runner_deve_adquirir_o_lock()
    {
        var result = await PostgresMigrationLock.TryAcquireAsync(
            fixture.ConnectionString, MigrationBoundary.Ledger, GenerousTimeout, ShortPoll, CancellationToken.None);

        Assert.Equal(LockAcquisitionOutcome.Acquired, result.Outcome);
        Assert.NotNull(result.Lock);

        await result.Lock!.ReleaseAsync(CancellationToken.None);
        await result.Lock.DisposeAsync();
    }

    [Fact]
    public async Task Segundo_runner_concorrente_para_a_mesma_fronteira_deve_ser_rejeitado_por_timeout()
    {
        var first = await PostgresMigrationLock.TryAcquireAsync(
            fixture.ConnectionString, MigrationBoundary.Ledger, GenerousTimeout, ShortPoll, CancellationToken.None);
        Assert.Equal(LockAcquisitionOutcome.Acquired, first.Outcome);

        try
        {
            var second = await PostgresMigrationLock.TryAcquireAsync(
                fixture.ConnectionString, MigrationBoundary.Ledger, ShortTimeout, ShortPoll, CancellationToken.None);

            Assert.Equal(LockAcquisitionOutcome.TimedOut, second.Outcome);
            Assert.Null(second.Lock);
            Assert.True(second.WaitDuration >= ShortTimeout);
        }
        finally
        {
            await first.Lock!.ReleaseAsync(CancellationToken.None);
            await first.Lock.DisposeAsync();
        }
    }

    [Fact]
    public async Task Lock_deve_liberar_apos_ReleaseAsync_bem_sucedido_permitindo_nova_aquisicao()
    {
        var first = await PostgresMigrationLock.TryAcquireAsync(
            fixture.ConnectionString, MigrationBoundary.Consolidation, GenerousTimeout, ShortPoll, CancellationToken.None);
        Assert.Equal(LockAcquisitionOutcome.Acquired, first.Outcome);

        await first.Lock!.ReleaseAsync(CancellationToken.None);
        await first.Lock.DisposeAsync();

        var second = await PostgresMigrationLock.TryAcquireAsync(
            fixture.ConnectionString, MigrationBoundary.Consolidation, ShortTimeout, ShortPoll, CancellationToken.None);

        Assert.Equal(LockAcquisitionOutcome.Acquired, second.Outcome);
        await second.Lock!.ReleaseAsync(CancellationToken.None);
        await second.Lock.DisposeAsync();
    }

    [Fact]
    public async Task Lock_deve_liberar_apos_encerramento_abrupto_da_conexao_sem_ReleaseAsync_explicito()
    {
        // Simula uma falha de processo/conexão: a conexão que detém o lock é
        // fechada diretamente (sem chamar ReleaseAsync) - prova que o
        // PostgreSQL libera o lock de sessão sozinho ao fim da sessão,
        // mesmo sem cooperação da aplicação (confirmado via documentação
        // oficial antes de implementar: pg_advisory_unlock_all "is
        // implicitly invoked at session end, even if the client
        // disconnects ungracefully").
        var first = await PostgresMigrationLock.TryAcquireAsync(
            fixture.ConnectionString, MigrationBoundary.Ledger, GenerousTimeout, ShortPoll, CancellationToken.None);
        Assert.Equal(LockAcquisitionOutcome.Acquired, first.Outcome);

        await first.Lock!.Connection.CloseAsync();

        var second = await PostgresMigrationLock.TryAcquireAsync(
            fixture.ConnectionString, MigrationBoundary.Ledger, GenerousTimeout, ShortPoll, CancellationToken.None);

        Assert.Equal(LockAcquisitionOutcome.Acquired, second.Outcome);
        await second.Lock!.ReleaseAsync(CancellationToken.None);
        await second.Lock.DisposeAsync();
    }

    [Fact]
    public async Task Ledger_e_Consolidation_devem_usar_locks_distintos_e_nunca_bloquear_um_ao_outro()
    {
        var ledgerLock = await PostgresMigrationLock.TryAcquireAsync(
            fixture.ConnectionString, MigrationBoundary.Ledger, ShortTimeout, ShortPoll, CancellationToken.None);
        var consolidationLock = await PostgresMigrationLock.TryAcquireAsync(
            fixture.ConnectionString, MigrationBoundary.Consolidation, ShortTimeout, ShortPoll, CancellationToken.None);

        try
        {
            Assert.Equal(LockAcquisitionOutcome.Acquired, ledgerLock.Outcome);
            Assert.Equal(LockAcquisitionOutcome.Acquired, consolidationLock.Outcome);
            Assert.NotEqual(PostgresMigrationLock.KeyForBoundary(MigrationBoundary.Ledger), PostgresMigrationLock.KeyForBoundary(MigrationBoundary.Consolidation));
        }
        finally
        {
            await ledgerLock.Lock!.ReleaseAsync(CancellationToken.None);
            await ledgerLock.Lock.DisposeAsync();
            await consolidationLock.Lock!.ReleaseAsync(CancellationToken.None);
            await consolidationLock.Lock.DisposeAsync();
        }
    }

    [Fact]
    public async Task Migrations_ja_aplicadas_permanecem_idempotentes_ao_rerodar_o_migrate()
    {
        var firstResult = await PostgresMigrationLock.TryAcquireAsync(
            fixture.ConnectionString, MigrationBoundary.Ledger, GenerousTimeout, ShortPoll, CancellationToken.None);
        Assert.Equal(LockAcquisitionOutcome.Acquired, firstResult.Outcome);

        await using (var context = DbContextFactory.Create(MigrationBoundary.Ledger, firstResult.Lock!.Connection))
        {
            await context.Database.MigrateAsync();
            var pendingAfterFirstRun = await context.Database.GetPendingMigrationsAsync();
            Assert.Empty(pendingAfterFirstRun);
        }

        await firstResult.Lock.ReleaseAsync(CancellationToken.None);
        await firstResult.Lock.DisposeAsync();

        // Segunda execução completa (lock + Migrate) contra o MESMO banco
        // já migrado - deve ser um no-op seguro, nunca lançar exceção.
        var secondResult = await PostgresMigrationLock.TryAcquireAsync(
            fixture.ConnectionString, MigrationBoundary.Ledger, GenerousTimeout, ShortPoll, CancellationToken.None);
        Assert.Equal(LockAcquisitionOutcome.Acquired, secondResult.Outcome);

        await using (var context = DbContextFactory.Create(MigrationBoundary.Ledger, secondResult.Lock!.Connection))
        {
            await context.Database.MigrateAsync();
            var pendingAfterSecondRun = await context.Database.GetPendingMigrationsAsync();
            Assert.Empty(pendingAfterSecondRun);
        }

        await secondResult.Lock.ReleaseAsync(CancellationToken.None);
        await secondResult.Lock.DisposeAsync();
    }
}

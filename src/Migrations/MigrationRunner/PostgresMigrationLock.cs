using System.Diagnostics;
using Npgsql;

namespace BancoCarrefour.MigrationRunner;

public enum LockAcquisitionOutcome
{
    Acquired,
    TimedOut,
}

public sealed record LockAcquisitionResult(LockAcquisitionOutcome Outcome, TimeSpan WaitDuration, PostgresMigrationLock? Lock);

/// <summary>
/// Exclusão mútua real em nível de banco (PostgreSQL session-level advisory
/// lock) para a execução de migrations. EF Core 8.0.11 (versão real
/// deste repositório, confirmada em Directory.Packages.props) NÃO tem o
/// locking automático introduzido apenas no EF Core 9
/// ("EF Core 9.0 introduces a locking mechanism to prevent multiple
/// simultaneous migration executions" - release notes oficiais do EF
/// Core) - por isso este lock é explícito e nunca dependente de
/// __EFMigrationsHistory (que só garante idempotência de reaplicação,
/// nunca exclusão mútua entre execuções concorrentes) nem apenas de
/// concurrency groups do GitHub Actions (guarda de orquestração útil, mas
/// não uma garantia no nível do banco).
///
/// Chave determinística de duas partes (pg_advisory_lock(key1 int, key2
/// int)) - key1 identifica a aplicação/namespace, key2 identifica a
/// fronteira (Ledger=1, Consolidation=2) - nunca a mesma chave para as
/// duas fronteiras, nunca dependente de hash de string (risco de colisão
/// desnecessário quando inteiros literais e estáveis já resolvem o
/// problema com garantia total).
///
/// Espera limitada implementada com "pg_try_advisory_lock" (não-bloqueante,
/// documentação oficial: "This will either obtain the lock immediately and
/// return true, or return false without wait if the lock cannot be
/// acquired immediately") em um loop de poll com timeout próprio - a
/// documentação oficial do PostgreSQL NÃO confirma que "lock_timeout"
/// (parâmetro de sessão) se aplica a locks consultivos (a descrição oficial
/// menciona apenas "table, index, row, or other database object"), então
/// este código nunca assume esse comportamento não documentado.
///
/// A conexão que adquire o lock é a MESMA conexão entregue ao EF Core
/// (ver MigrateCommand: UseNpgsql(lock.Connection, contextOwnsConnection:
/// false)) - nunca uma conexão descartável separada da que executa as
/// migrations, exatamente o erro que esta implementação evita.
///
/// Liberação automática: locks de sessão são "automatically cleaned up by
/// the server at the end of the session" mesmo em desconexão não-graciosa
/// (pg_advisory_unlock_all "is implicitly invoked at session end, even if
/// the client disconnects ungracefully") - confirmado via documentação
/// oficial do PostgreSQL 16 (functions-admin.html) antes de implementar.
/// </summary>
public sealed class PostgresMigrationLock : IAsyncDisposable
{
    // Namespace fixo desta aplicação - nunca reutilizado por outro sistema
    // dentro da mesma instância PostgreSQL.
    private const int AppNamespaceKey = 875213;

    private const int LedgerBoundaryKey = 1;
    private const int ConsolidationBoundaryKey = 2;

    private readonly int key2;
    private bool locked;

    private PostgresMigrationLock(NpgsqlConnection connection, int key2, bool locked)
    {
        Connection = connection;
        this.key2 = key2;
        this.locked = locked;
    }

    public NpgsqlConnection Connection { get; }

    public static int KeyForBoundary(MigrationBoundary boundary) => boundary switch
    {
        MigrationBoundary.Ledger => LedgerBoundaryKey,
        MigrationBoundary.Consolidation => ConsolidationBoundaryKey,
        _ => throw new ArgumentOutOfRangeException(nameof(boundary)),
    };

    public static async Task<LockAcquisitionResult> TryAcquireAsync(
        string connectionString,
        MigrationBoundary boundary,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        var key2 = KeyForBoundary(boundary);

        // Pooling=false explícito: um pool do lado do cliente (padrão do
        // Npgsql) manteria a conexão física viva mesmo após CloseAsync()
        // (ela só volta para o pool, o servidor nunca vê a desconexão) -
        // isso quebraria exatamente a garantia de liberação automática que
        // este lock depende ("released even on ungraceful disconnect"),
        // porque a sessão do PostgreSQL nunca terminaria de verdade
        // enquanto o processo mantém o pool vivo. Achado real durante a
        // validação desta auditoria - sem pooling, cada execução do
        // MigrationRunner (um processo one-off que nunca reutiliza a
        // conexão) fecha a sessão de verdade ao terminar, mesmo em falha
        // abrupta do processo.
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        var connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            bool acquired;
            await using (var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key1, @key2)", connection))
            {
                command.Parameters.AddWithValue("key1", AppNamespaceKey);
                command.Parameters.AddWithValue("key2", key2);
                acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
            }

            if (acquired)
            {
                stopwatch.Stop();
                return new LockAcquisitionResult(
                    LockAcquisitionOutcome.Acquired,
                    stopwatch.Elapsed,
                    new PostgresMigrationLock(connection, key2, locked: true));
            }

            if (stopwatch.Elapsed >= timeout)
            {
                stopwatch.Stop();
                await connection.DisposeAsync();
                return new LockAcquisitionResult(LockAcquisitionOutcome.TimedOut, stopwatch.Elapsed, null);
            }

            await Task.Delay(pollInterval, cancellationToken);
        }
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        if (!locked)
        {
            return;
        }

        await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key1, @key2)", Connection);
        command.Parameters.AddWithValue("key1", AppNamespaceKey);
        command.Parameters.AddWithValue("key2", key2);
        await command.ExecuteScalarAsync(cancellationToken);
        locked = false;
    }

    public async ValueTask DisposeAsync()
    {
        // pg_advisory_unlock_all é implicitamente invocado ao fim da sessão
        // (mesmo em desconexão abrupta) - fechar a conexão já libera o lock
        // mesmo que ReleaseAsync não tenha sido chamado explicitamente
        // (ex.: processo encerrado por falha antes de chegar lá).
        await Connection.DisposeAsync();
    }
}

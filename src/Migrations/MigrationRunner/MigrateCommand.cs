using System.Diagnostics;
using BancoCarrefour.Contracts.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BancoCarrefour.MigrationRunner;

/// <summary>
/// Comando "migrate" - o único caminho executado automaticamente pelos
/// workflows de deploy AWS. Só aplica migrations fase EXPAND; qualquer
/// migration pendente fase CONTRACT nesta fronteira faz o comando recusar
/// e sair sem aplicar nada (nunca aplica CONTRACT implicitamente - ver
///, seção 4).
/// </summary>
public static class MigrateCommand
{
    public static async Task<int> RunAsync(CliOptions options, CancellationToken cancellationToken)
    {
        var boundary = options.Boundary!.Value;
        var boundaryName = boundary.ToString();

        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        string connectionString;
        try
        {
            connectionString = DbContextFactory.ResolveConnectionString(boundary, configuration, options.Environment, options.SecretName);
        }
        catch (Exception ex)
        {
            StructuredLog.Emit("migration.configuration_error", new Dictionary<string, object?>
            {
                ["boundary"] = boundaryName,
                ["environment"] = options.Environment,
                ["release_id"] = options.ReleaseId,
                ["source_commit"] = options.SourceCommit,
                ["error"] = ex.GetType().Name,
                // Nunca a mensagem completa da exceção sem checagem: connection
                // strings do Npgsql podem aparecer em mensagens de erro de
                // conexão - resumimos ao tipo da exceção apenas.
            });
            return ExitCode.UsageOrConfigurationError;
        }

        StructuredLog.Emit("migration.started", new Dictionary<string, object?>
        {
            ["boundary"] = boundaryName,
            ["environment"] = options.Environment,
            ["release_id"] = options.ReleaseId,
            ["source_commit"] = options.SourceCommit,
            ["phase"] = nameof(MigrationPhase.Expand),
        });

        var lockResult = await PostgresMigrationLock.TryAcquireAsync(
            connectionString,
            boundary,
            TimeSpan.FromSeconds(options.LockTimeoutSeconds),
            TimeSpan.FromMilliseconds(options.PollIntervalMilliseconds),
            cancellationToken);

        StructuredLog.Emit("migration.lock_outcome", new Dictionary<string, object?>
        {
            ["boundary"] = boundaryName,
            ["release_id"] = options.ReleaseId,
            ["outcome"] = lockResult.Outcome.ToString(),
            ["lock_wait_duration_ms"] = lockResult.WaitDuration.TotalMilliseconds,
        });

        if (lockResult.Outcome == LockAcquisitionOutcome.TimedOut)
        {
            return ExitCode.LockTimedOut;
        }

        await using var migrationLock = lockResult.Lock!;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var context = DbContextFactory.Create(boundary, migrationLock.Connection);

            var phasesById = MigrationPhaseCatalog.Build(DbContextFactory.MigrationsAssembly(boundary));
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

            var nonExpand = pending.Where(id => !phasesById.TryGetValue(id, out var phase) || phase != MigrationPhase.Expand).ToList();
            if (nonExpand.Count > 0)
            {
                StructuredLog.Emit("migration.refused_unapproved_phase", new Dictionary<string, object?>
                {
                    ["boundary"] = boundaryName,
                    ["release_id"] = options.ReleaseId,
                    ["pending_non_expand_migrations"] = nonExpand,
                });
                await migrationLock.ReleaseAsync(cancellationToken);
                return ExitCode.RefusedUnapprovedPhase;
            }

            if (pending.Count == 0)
            {
                StructuredLog.Emit("migration.completed", new Dictionary<string, object?>
                {
                    ["boundary"] = boundaryName,
                    ["release_id"] = options.ReleaseId,
                    ["source_commit"] = options.SourceCommit,
                    ["applied_migrations"] = Array.Empty<string>(),
                    ["duration_ms"] = stopwatch.Elapsed.TotalMilliseconds,
                    ["note"] = "no_pending_migrations",
                });
                await migrationLock.ReleaseAsync(cancellationToken);
                return ExitCode.Success;
            }

            await context.Database.MigrateAsync(cancellationToken);
            stopwatch.Stop();

            StructuredLog.Emit("migration.completed", new Dictionary<string, object?>
            {
                ["boundary"] = boundaryName,
                ["release_id"] = options.ReleaseId,
                ["source_commit"] = options.SourceCommit,
                ["applied_migrations"] = pending,
                ["duration_ms"] = stopwatch.Elapsed.TotalMilliseconds,
            });

            await migrationLock.ReleaseAsync(cancellationToken);
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            StructuredLog.Emit("migration.failed", new Dictionary<string, object?>
            {
                ["boundary"] = boundaryName,
                ["release_id"] = options.ReleaseId,
                ["source_commit"] = options.SourceCommit,
                ["duration_ms"] = stopwatch.Elapsed.TotalMilliseconds,
                // Nunca ex.Message aqui (mesma razão do bloco catch em
                // ResolveConnectionString acima): uma falha durante
                // MigrateAsync() pode vir do Npgsql, cuja mensagem de erro
                // de conexão pode conter host/porta/usuário. O tipo da
                // exceção já basta como métrica/alarme (ver
                // docs/operations/runbook-implantacao-aws.md, seção 8) -
                // o log completo de execução no CloudWatch (fora deste
                // evento resumido) permanece disponível para diagnóstico.
                ["error_type"] = ex.GetType().Name,
            });

            try
            {
                await migrationLock.ReleaseAsync(cancellationToken);
            }
            catch
            {
                // A conexão pode já estar em estado inválido após a falha da
                // migration - o lock ainda será liberado automaticamente pelo
                // servidor ao fim da sessão (DisposeAsync fecha a conexão
                // logo em seguida).
            }

            return ExitCode.MigrationFailed;
        }
    }
}

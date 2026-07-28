using System.Diagnostics;
using BancoCarrefour.Contracts.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BancoCarrefour.MigrationRunner;

/// <summary>
/// Comando "contract" - procedimento separado e protegido para aplicar
/// migrations fase CONTRACT (remoção/renomeação/NOT NULL). NUNCA invocado
/// pelos workflows de deploy automático (deploy-development.yml,
/// promote-staging.yml, promote-production.yml) - só por uma execução
/// manual explícita, exigindo --approved-by e --compatibility-window-closed
///.
/// Nenhuma migration CONTRACT existe neste repositório ainda - este comando
/// materializa a capacidade/gate, não uma execução real.
/// </summary>
public static class ContractCommand
{
    public static async Task<int> RunAsync(CliOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApprovedBy) || !options.CompatibilityWindowClosed)
        {
            StructuredLog.Emit("migration.contract_refused_missing_approval", new Dictionary<string, object?>
            {
                ["boundary"] = options.Boundary?.ToString(),
                ["release_id"] = options.ReleaseId,
                ["approved_by_present"] = !string.IsNullOrWhiteSpace(options.ApprovedBy),
                ["compatibility_window_closed_present"] = options.CompatibilityWindowClosed,
            });
            return ExitCode.UsageOrConfigurationError;
        }

        var boundary = options.Boundary!.Value;
        var boundaryName = boundary.ToString();

        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var connectionString = DbContextFactory.ResolveConnectionString(boundary, configuration, options.Environment, options.SecretName);

        StructuredLog.Emit("migration.contract_started", new Dictionary<string, object?>
        {
            ["boundary"] = boundaryName,
            ["release_id"] = options.ReleaseId,
            ["source_commit"] = options.SourceCommit,
            ["approved_by"] = options.ApprovedBy,
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

            var pendingNonContract = pending.Where(id => phasesById.TryGetValue(id, out var phase) && phase != MigrationPhase.Contract).ToList();
            if (pendingNonContract.Count > 0)
            {
                StructuredLog.Emit("migration.contract_refused_expand_pending", new Dictionary<string, object?>
                {
                    ["boundary"] = boundaryName,
                    ["release_id"] = options.ReleaseId,
                    ["pending_non_contract_migrations"] = pendingNonContract,
                });
                await migrationLock.ReleaseAsync(cancellationToken);
                return ExitCode.RefusedUnapprovedPhase;
            }

            var pendingContract = pending.Where(id => phasesById.TryGetValue(id, out var phase) && phase == MigrationPhase.Contract).ToList();
            if (pendingContract.Count == 0)
            {
                StructuredLog.Emit("migration.contract_completed", new Dictionary<string, object?>
                {
                    ["boundary"] = boundaryName,
                    ["release_id"] = options.ReleaseId,
                    ["applied_migrations"] = Array.Empty<string>(),
                    ["duration_ms"] = stopwatch.Elapsed.TotalMilliseconds,
                    ["note"] = "no_pending_contract_migrations",
                });
                await migrationLock.ReleaseAsync(cancellationToken);
                return ExitCode.Success;
            }

            await context.Database.MigrateAsync(cancellationToken);
            stopwatch.Stop();

            StructuredLog.Emit("migration.contract_completed", new Dictionary<string, object?>
            {
                ["boundary"] = boundaryName,
                ["release_id"] = options.ReleaseId,
                ["approved_by"] = options.ApprovedBy,
                ["applied_migrations"] = pendingContract,
                ["duration_ms"] = stopwatch.Elapsed.TotalMilliseconds,
            });

            await migrationLock.ReleaseAsync(cancellationToken);
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            StructuredLog.Emit("migration.contract_failed", new Dictionary<string, object?>
            {
                ["boundary"] = boundaryName,
                ["release_id"] = options.ReleaseId,
                ["duration_ms"] = stopwatch.Elapsed.TotalMilliseconds,
                // Nunca ex.Message - mesma razão documentada em
                // MigrateCommand.cs (mensagens de erro do Npgsql podem
                // conter host/porta/usuário).
                ["error_type"] = ex.GetType().Name,
            });

            try
            {
                await migrationLock.ReleaseAsync(cancellationToken);
            }
            catch
            {
                // Ver comentário equivalente em MigrateCommand.
            }

            return ExitCode.MigrationFailed;
        }
    }
}

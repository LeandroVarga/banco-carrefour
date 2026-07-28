using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BancoCarrefour.MigrationRunner;

/// <summary>
/// Comando "backfill" - runner genérico de tarefas <see cref="IBackfillTask"/>
/// em lotes limitados, com checkpoint persistido (restartability) e
/// timeout próprio. O catálogo de tarefas está vazio hoje (ver
/// <see cref="Tasks"/>) - nenhum backfill real é necessário neste
/// repositório ainda; invocar este comando hoje apenas confirma que não há
/// trabalho pendente, honestamente, sem fabricar uma tarefa fictícia.
/// </summary>
public static class BackfillCommand
{
    private const int DefaultBatchSize = 500;

    // Catálogo real de tarefas de backfill registradas - vazio até que uma
    // migration EXPAND real precise de um preenchimento de dados.
    private static readonly IReadOnlyList<IBackfillTask> Tasks = [];

    public static async Task<int> RunAsync(CliOptions options, CancellationToken cancellationToken)
    {
        var boundary = options.Boundary!.Value;
        var boundaryName = boundary.ToString();
        var tasksForBoundary = Tasks.Where(t => t.Boundary == boundary).ToList();

        if (tasksForBoundary.Count == 0)
        {
            StructuredLog.Emit("migration.backfill_no_tasks_registered", new Dictionary<string, object?>
            {
                ["boundary"] = boundaryName,
                ["release_id"] = options.ReleaseId,
            });
            return ExitCode.Success;
        }

        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var connectionString = DbContextFactory.ResolveConnectionString(boundary, configuration, options.Environment, options.SecretName);

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
        var overallTimeout = TimeSpan.FromSeconds(options.LockTimeoutSeconds);
        var overallStopwatch = Stopwatch.StartNew();

        try
        {
            await EnsureCheckpointTableAsync(migrationLock.Connection, cancellationToken);
            await using var context = DbContextFactory.Create(boundary, migrationLock.Connection);

            foreach (var task in tasksForBoundary)
            {
                var checkpoint = await ReadCheckpointAsync(migrationLock.Connection, boundaryName, task.Name, cancellationToken);
                var totalRows = 0;

                while (true)
                {
                    if (overallStopwatch.Elapsed >= overallTimeout)
                    {
                        StructuredLog.Emit("migration.backfill_timed_out", new Dictionary<string, object?>
                        {
                            ["boundary"] = boundaryName,
                            ["task"] = task.Name,
                            ["release_id"] = options.ReleaseId,
                            ["rows_processed_so_far"] = totalRows,
                            ["duration_ms"] = overallStopwatch.Elapsed.TotalMilliseconds,
                        });
                        return ExitCode.BackfillIncomplete;
                    }

                    var batch = await task.ProcessBatchAsync(context, checkpoint, DefaultBatchSize, cancellationToken);
                    totalRows += batch.RowsProcessed;

                    if (batch.RowsProcessed == 0)
                    {
                        break;
                    }

                    checkpoint = batch.NextCheckpoint;
                    await WriteCheckpointAsync(migrationLock.Connection, boundaryName, task.Name, checkpoint, cancellationToken);

                    StructuredLog.Emit("migration.backfill_progress", new Dictionary<string, object?>
                    {
                        ["boundary"] = boundaryName,
                        ["task"] = task.Name,
                        ["release_id"] = options.ReleaseId,
                        ["rows_processed_in_batch"] = batch.RowsProcessed,
                        ["rows_processed_total"] = totalRows,
                        ["checkpoint"] = checkpoint,
                    });
                }

                StructuredLog.Emit("migration.backfill_task_completed", new Dictionary<string, object?>
                {
                    ["boundary"] = boundaryName,
                    ["task"] = task.Name,
                    ["release_id"] = options.ReleaseId,
                    ["rows_processed_total"] = totalRows,
                    ["duration_ms"] = overallStopwatch.Elapsed.TotalMilliseconds,
                });
            }

            await migrationLock.ReleaseAsync(cancellationToken);
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            StructuredLog.Emit("migration.backfill_failed", new Dictionary<string, object?>
            {
                ["boundary"] = boundaryName,
                ["release_id"] = options.ReleaseId,
                // Nunca ex.Message - mesma razão documentada em
                // MigrateCommand.cs (mensagens de erro do Npgsql podem
                // conter host/porta/usuário).
                ["error_type"] = ex.GetType().Name,
                ["duration_ms"] = overallStopwatch.Elapsed.TotalMilliseconds,
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

    // Tabela operacional própria do MigrationRunner (nunca uma EF Core
    // migration - é infraestrutura do próprio runner, não do domínio) -
    // criada de forma idempotente (IF NOT EXISTS) para persistir o
    // checkpoint de cada tarefa de backfill, permitindo retomar de onde
    // parou após uma falha/timeout.
    private static async Task EnsureCheckpointTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS migration_runner_backfill_checkpoints (
                boundary text NOT NULL,
                task_name text NOT NULL,
                checkpoint text NULL,
                updated_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (boundary, task_name)
            )
            """,
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> ReadCheckpointAsync(NpgsqlConnection connection, string boundary, string taskName, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT checkpoint FROM migration_runner_backfill_checkpoints WHERE boundary = @boundary AND task_name = @task_name",
            connection);
        command.Parameters.AddWithValue("boundary", boundary);
        command.Parameters.AddWithValue("task_name", taskName);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    private static async Task WriteCheckpointAsync(NpgsqlConnection connection, string boundary, string taskName, string? checkpoint, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO migration_runner_backfill_checkpoints (boundary, task_name, checkpoint, updated_at)
            VALUES (@boundary, @task_name, @checkpoint, now())
            ON CONFLICT (boundary, task_name)
            DO UPDATE SET checkpoint = EXCLUDED.checkpoint, updated_at = now()
            """,
            connection);
        command.Parameters.AddWithValue("boundary", boundary);
        command.Parameters.AddWithValue("task_name", taskName);
        command.Parameters.AddWithValue("checkpoint", (object?)checkpoint ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

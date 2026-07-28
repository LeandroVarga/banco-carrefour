using System.Data;
using BancoCarrefour.Ledger.Application.PublishOutbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BancoCarrefour.Ledger.Infrastructure.Outbox;

public sealed class PostgresOutboxStore(
    LedgerDbContext dbContext,
    TimeProvider timeProvider) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxEvent>> ClaimNextBatchAsync(
        int batchSize,
        string lockedBy,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expiresBefore = now - claimTimeout;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            UPDATE outbox_messages
            SET
                status = 'Processing',
                locked_at = @now,
                locked_by = @lockedBy,
                attempts = attempts + 1,
                last_error = NULL
            WHERE outbox_id IN (
                SELECT outbox_id
                FROM outbox_messages
                WHERE
                    (
                        status = 'Pending'
                        AND (next_attempt_at IS NULL OR next_attempt_at <= @now)
                    )
                    OR (
                        status = 'Processing'
                        AND locked_at IS NOT NULL
                        AND locked_at < @expiresBefore
                    )
                ORDER BY created_at
                LIMIT @batchSize
                FOR UPDATE SKIP LOCKED
            )
            RETURNING outbox_id, event_id, event_type, event_version, payload, attempts, created_at;
            """;

        AddParameter(command, "now", now);
        AddParameter(command, "lockedBy", lockedBy);
        AddParameter(command, "expiresBefore", expiresBefore);
        AddParameter(command, "batchSize", Math.Max(1, batchSize));

        var claimed = new List<OutboxEvent>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                claimed.Add(new OutboxEvent(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.GetInt32(5),
                    reader.GetFieldValue<DateTimeOffset>(6)));
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return claimed;
    }

    public async Task MarkPublishedAsync(
        Guid outboxId,
        string lockedBy,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken)
    {
        var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE outbox_messages
            SET
                status = 'Published',
                published_at = {publishedAt.ToUniversalTime()},
                locked_at = NULL,
                locked_by = NULL,
                next_attempt_at = NULL,
                last_error = NULL
            WHERE outbox_id = {outboxId}
              AND status = 'Processing'
              AND locked_by = {lockedBy};
            """, cancellationToken);

        if (rowsAffected != 1)
        {
            throw new InvalidOperationException("Mensagem da Outbox não estava reclamada pela instância atual.");
        }
    }

    public async Task MarkFailedAsync(
        OutboxFailure failure,
        string lockedBy,
        CancellationToken cancellationToken)
    {
        var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE outbox_messages
            SET
                status = 'Pending',
                locked_at = NULL,
                locked_by = NULL,
                next_attempt_at = {failure.NextAttemptAt.ToUniversalTime()},
                last_error = {failure.Error}
            WHERE outbox_id = {failure.OutboxId}
              AND status = 'Processing'
              AND locked_by = {lockedBy};
            """, cancellationToken);

        if (rowsAffected != 1)
        {
            throw new InvalidOperationException("Mensagem da Outbox não estava reclamada pela instância atual.");
        }
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

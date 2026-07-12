using BancoCarrefour.Consolidation.Persistence;
using BancoCarrefour.Consolidation.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Globalization;

namespace BancoCarrefour.Consolidation.Application;

public sealed class EntryCreatedProjectionProcessor(
    ConsolidationDbContext dbContext,
    TimeProvider timeProvider)
{
    private const string EventType = "EntryCreated";
    private const int EventVersion = 1;
    private const string Currency = "BRL";
    private const string ProcessedEventUniqueConstraintName = "IX_processed_events_event_id";

    public async Task<ProjectionResult> ProcessAsync(
        EntryCreatedEvent message,
        CancellationToken cancellationToken)
    {
        var validated = Validate(message);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var processedAt = timeProvider.GetUtcNow();

        dbContext.ProcessedEvents.Add(new ProcessedEvent
        {
            ProcessedEventId = Guid.NewGuid(),
            EventId = message.EventId,
            EventType = message.EventType,
            EventVersion = message.EventVersion,
            MerchantId = message.MerchantId,
            BusinessDate = validated.BusinessDate,
            ProcessedAt = processedAt
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            var rowsAffected = await UpsertDailyBalanceAsync(validated, message.OccurredAt.ToUniversalTime(), processedAt, cancellationToken);

            if (rowsAffected != 1)
            {
                throw new ProjectionValidationException("currency do DailyBalance existente diverge do evento.");
            }

            var dailyBalance = await dbContext.DailyBalances
                .AsNoTracking()
                .SingleAsync(
                    x => x.MerchantId == validated.MerchantId && x.BusinessDate == validated.BusinessDate,
                    cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new ProjectionResult(
                Applied: true,
                Duplicate: false,
                DailyBalanceId: dailyBalance.DailyBalanceId,
                BusinessDate: dailyBalance.BusinessDate);
        }
        catch (DbUpdateException exception) when (IsProcessedEventUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            return await CreateDuplicateResultAsync(validated, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<ProjectionResult> CreateDuplicateResultAsync(
        ValidatedEntryCreatedEvent validated,
        CancellationToken cancellationToken)
    {
        var dailyBalance = await dbContext.DailyBalances
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.MerchantId == validated.MerchantId && x.BusinessDate == validated.BusinessDate,
                cancellationToken);

        return new ProjectionResult(
            Applied: false,
            Duplicate: true,
            DailyBalanceId: dailyBalance?.DailyBalanceId,
            BusinessDate: validated.BusinessDate);
    }

    private Task<int> UpsertDailyBalanceAsync(
        ValidatedEntryCreatedEvent validated,
        DateTimeOffset occurredAt,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken)
    {
        var dailyBalanceId = Guid.NewGuid();
        var creditIncrement = validated.Type == "CREDIT" ? validated.Amount : 0m;
        var debitIncrement = validated.Type == "DEBIT" ? validated.Amount : 0m;
        var balanceIncrement = validated.Type == "CREDIT" ? validated.Amount : -validated.Amount;

        return dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO daily_balances (
                daily_balance_id,
                merchant_id,
                business_date,
                total_credits,
                total_debits,
                balance,
                currency,
                entry_count,
                last_event_occurred_at,
                last_updated_at
            )
            VALUES (
                {dailyBalanceId},
                {validated.MerchantId},
                {validated.BusinessDate},
                {creditIncrement},
                {debitIncrement},
                {balanceIncrement},
                {Currency},
                {1L},
                {occurredAt},
                {processedAt}
            )
            ON CONFLICT (merchant_id, business_date) DO UPDATE
            SET
                total_credits = daily_balances.total_credits + EXCLUDED.total_credits,
                total_debits = daily_balances.total_debits + EXCLUDED.total_debits,
                balance = daily_balances.balance + EXCLUDED.balance,
                entry_count = daily_balances.entry_count + 1,
                last_event_occurred_at = GREATEST(daily_balances.last_event_occurred_at, EXCLUDED.last_event_occurred_at),
                last_updated_at = EXCLUDED.last_updated_at
            WHERE daily_balances.currency = EXCLUDED.currency;
            """, cancellationToken);
    }

    private static ValidatedEntryCreatedEvent Validate(EntryCreatedEvent message)
    {
        if (message.EventType != EventType)
        {
            throw new ProjectionValidationException("eventType deve ser EntryCreated.");
        }

        if (message.EventVersion != EventVersion)
        {
            throw new ProjectionValidationException("eventVersion deve ser 1.");
        }

        if (message.Type is not ("CREDIT" or "DEBIT"))
        {
            throw new ProjectionValidationException("type deve ser CREDIT ou DEBIT.");
        }

        if (!decimal.TryParse(message.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            throw new ProjectionValidationException("amount deve ser decimal positivo.");
        }

        if (message.Currency != Currency)
        {
            throw new ProjectionValidationException("currency deve ser BRL.");
        }

        if (string.IsNullOrWhiteSpace(message.MerchantId) || message.MerchantId.Length > 64)
        {
            throw new ProjectionValidationException("merchantId é obrigatório e deve ter no máximo 64 caracteres.");
        }

        if (!DateOnly.TryParseExact(message.BusinessDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var businessDate))
        {
            throw new ProjectionValidationException("businessDate deve ser uma data válida no formato yyyy-MM-dd.");
        }

        return new ValidatedEntryCreatedEvent(
            message.MerchantId,
            businessDate,
            message.Type,
            amount);
    }

    private static bool IsProcessedEventUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName == ProcessedEventUniqueConstraintName;
    }

    private sealed record ValidatedEntryCreatedEvent(
        string MerchantId,
        DateOnly BusinessDate,
        string Type,
        decimal Amount);
}

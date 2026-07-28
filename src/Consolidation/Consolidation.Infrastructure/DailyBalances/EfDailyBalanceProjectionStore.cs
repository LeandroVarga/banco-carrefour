using BancoCarrefour.Consolidation.Application;
using BancoCarrefour.Consolidation.Application.ApplyFinancialEntry;
using BancoCarrefour.Consolidation.Domain;
using BancoCarrefour.Consolidation.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BancoCarrefour.Consolidation.Infrastructure.DailyBalances;

public sealed class EfDailyBalanceProjectionStore(
    ConsolidationDbContext dbContext) : IDailyBalanceProjectionStore
{
    private const string Currency = Money.SupportedCurrency;
    private const string ProcessedEventUniqueConstraintName = "IX_processed_events_event_id";

    public async Task<ProjectionResult> ApplyAsync(
        ProcessedFinancialEntry processedEntry,
        DailyBalanceContribution contribution,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.ProcessedEvents.Add(new ProcessedEvent
        {
            ProcessedEventId = Guid.NewGuid(),
            EventId = processedEntry.EventId,
            EventType = processedEntry.EventType,
            EventVersion = processedEntry.EventVersion,
            MerchantId = processedEntry.MerchantId,
            BusinessDate = processedEntry.BusinessDate,
            ProcessedAt = processedEntry.ProcessedAt
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            var rowsAffected = await UpsertDailyBalanceAsync(contribution, processedEntry.ProcessedAt, cancellationToken);
            if (rowsAffected != 1)
            {
                throw new ProjectionValidationException("currency do DailyBalance existente diverge do evento.");
            }

            var dailyBalance = await dbContext.DailyBalances
                .AsNoTracking()
                .SingleAsync(
                    x => x.MerchantId == contribution.MerchantId.Value
                        && x.BusinessDate == contribution.BusinessDate.Value,
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

            return await CreateDuplicateResultAsync(contribution, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private Task<int> UpsertDailyBalanceAsync(
        DailyBalanceContribution contribution,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken)
    {
        var dailyBalanceId = Guid.NewGuid();

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
                {contribution.MerchantId.Value},
                {contribution.BusinessDate.Value},
                {contribution.CreditAmount},
                {contribution.DebitAmount},
                {contribution.BalanceAmount},
                {Currency},
                {1L},
                {contribution.OccurredAt.ToUniversalTime()},
                {processedAt.ToUniversalTime()}
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

    private async Task<ProjectionResult> CreateDuplicateResultAsync(
        DailyBalanceContribution contribution,
        CancellationToken cancellationToken)
    {
        var dailyBalance = await dbContext.DailyBalances
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.MerchantId == contribution.MerchantId.Value
                    && x.BusinessDate == contribution.BusinessDate.Value,
                cancellationToken);

        return new ProjectionResult(
            Applied: false,
            Duplicate: true,
            DailyBalanceId: dailyBalance?.DailyBalanceId,
            BusinessDate: contribution.BusinessDate.Value);
    }

    private static bool IsProcessedEventUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName == ProcessedEventUniqueConstraintName;
    }
}

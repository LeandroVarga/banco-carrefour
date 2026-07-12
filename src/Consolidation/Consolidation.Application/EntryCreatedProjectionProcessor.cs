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

        var alreadyProcessed = await dbContext.ProcessedEvents
            .AsNoTracking()
            .AnyAsync(x => x.EventId == message.EventId, cancellationToken);

        if (alreadyProcessed)
        {
            await transaction.CommitAsync(cancellationToken);

            return await CreateDuplicateResultAsync(validated, cancellationToken);
        }

        var dailyBalance = await dbContext.DailyBalances
            .SingleOrDefaultAsync(
                x => x.MerchantId == message.MerchantId && x.BusinessDate == validated.BusinessDate,
                cancellationToken);

        if (dailyBalance is null)
        {
            dailyBalance = new DailyBalance
            {
                DailyBalanceId = Guid.NewGuid(),
                MerchantId = message.MerchantId,
                BusinessDate = validated.BusinessDate,
                Currency = Currency,
                LastEventOccurredAt = message.OccurredAt.ToUniversalTime()
            };

            dbContext.DailyBalances.Add(dailyBalance);
        }

        ApplyAmount(dailyBalance, validated);
        dailyBalance.EntryCount += 1;
        dailyBalance.LastEventOccurredAt = Max(dailyBalance.LastEventOccurredAt, message.OccurredAt.ToUniversalTime());
        dailyBalance.LastUpdatedAt = timeProvider.GetUtcNow();

        dbContext.ProcessedEvents.Add(new ProcessedEvent
        {
            ProcessedEventId = Guid.NewGuid(),
            EventId = message.EventId,
            EventType = message.EventType,
            EventVersion = message.EventVersion,
            MerchantId = message.MerchantId,
            BusinessDate = validated.BusinessDate,
            ProcessedAt = timeProvider.GetUtcNow()
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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

        return new ProjectionResult(
            Applied: true,
            Duplicate: false,
            DailyBalanceId: dailyBalance.DailyBalanceId,
            BusinessDate: dailyBalance.BusinessDate);
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

    private static void ApplyAmount(DailyBalance dailyBalance, ValidatedEntryCreatedEvent validated)
    {
        if (validated.Type == "CREDIT")
        {
            dailyBalance.TotalCredits += validated.Amount;
            dailyBalance.Balance += validated.Amount;
            return;
        }

        dailyBalance.TotalDebits += validated.Amount;
        dailyBalance.Balance -= validated.Amount;
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

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second)
    {
        return first >= second ? first : second;
    }

    private sealed record ValidatedEntryCreatedEvent(
        string MerchantId,
        DateOnly BusinessDate,
        string Type,
        decimal Amount);
}

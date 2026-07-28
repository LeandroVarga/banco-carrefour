using System.Globalization;
using System.Text.Json;
using BancoCarrefour.Contracts.Events;
using BancoCarrefour.Ledger.Application.RegisterFinancialEntry;
using BancoCarrefour.Ledger.Domain;
using BancoCarrefour.Ledger.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using DomainEntryType = BancoCarrefour.Ledger.Domain.EntryType;
using PersistenceEntryType = BancoCarrefour.Ledger.Infrastructure.Entities.EntryType;

namespace BancoCarrefour.Ledger.Infrastructure.FinancialEntries;

public sealed class EfFinancialEntryRegistrationStore(LedgerDbContext dbContext) : IFinancialEntryRegistrationStore
{
    private const string InputIdempotencyUniqueConstraintName = "IX_input_idempotency_merchant_id_idempotency_key";
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RegisterFinancialEntryResult> RegisterAsync(
        FinancialEntryRegistration registration,
        CancellationToken cancellationToken)
    {
        var existing = await FindExistingResponseAsync(
            registration.Entry.MerchantId.Value,
            registration.IdempotencyKey,
            registration.PayloadFingerprint,
            cancellationToken);

        if (existing.IsConflict)
        {
            return RegisterFinancialEntryResult.Conflict();
        }

        if (existing.Response is not null)
        {
            return RegisterFinancialEntryResult.Replay(existing.Response);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Entries.Add(ToPersistenceEntry(registration.Entry));
        dbContext.InputIdempotencyRecords.Add(new InputIdempotency
        {
            InputIdempotencyId = Guid.NewGuid(),
            MerchantId = registration.Entry.MerchantId.Value,
            IdempotencyKey = registration.IdempotencyKey,
            PayloadFingerprint = registration.PayloadFingerprint,
            EntryId = registration.Entry.EntryId.Value,
            CreatedAt = registration.Entry.RegisteredAt
        });
        dbContext.OutboxMessages.Add(ToOutboxMessage(registration));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return RegisterFinancialEntryResult.Created(
                RegisterFinancialEntryUseCase.ToRegisteredEntry(registration.Entry, registration.IdempotencyKey));
        }
        catch (DbUpdateException exception) when (IsInputIdempotencyUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            var concurrentExisting = await FindExistingResponseAsync(
                registration.Entry.MerchantId.Value,
                registration.IdempotencyKey,
                registration.PayloadFingerprint,
                cancellationToken);

            return concurrentExisting.Response is not null
                ? RegisterFinancialEntryResult.Replay(concurrentExisting.Response)
                : RegisterFinancialEntryResult.Conflict();
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<(RegisteredFinancialEntry? Response, bool IsConflict)> FindExistingResponseAsync(
        string merchantId,
        string idempotencyKey,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.InputIdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.MerchantId == merchantId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existing is null)
        {
            return (null, false);
        }

        if (existing.PayloadFingerprint != fingerprint)
        {
            return (null, true);
        }

        var entry = await dbContext.Entries
            .AsNoTracking()
            .SingleAsync(x => x.EntryId == existing.EntryId, cancellationToken);

        return (new RegisteredFinancialEntry(
            entry.EntryId,
            entry.MerchantId,
            entry.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            entry.Type == PersistenceEntryType.Credit ? "CREDIT" : "DEBIT",
            entry.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            entry.Currency,
            entry.OccurredAt.ToUniversalTime(),
            entry.CreatedAt.ToUniversalTime(),
            existing.IdempotencyKey), false);
    }

    private static Entry ToPersistenceEntry(FinancialEntry entry)
    {
        return new Entry
        {
            EntryId = entry.EntryId.Value,
            MerchantId = entry.MerchantId.Value,
            BusinessDate = entry.BusinessDate.Value,
            Type = entry.Type == DomainEntryType.Credit ? PersistenceEntryType.Credit : PersistenceEntryType.Debit,
            Amount = entry.Money.Amount,
            Currency = entry.Money.Currency,
            OccurredAt = entry.OccurredAt,
            CreatedAt = entry.RegisteredAt,
            Description = entry.Description.Value
        };
    }

    private static OutboxMessage ToOutboxMessage(FinancialEntryRegistration registration)
    {
        var integrationEvent = registration.IntegrationEvent;
        var payload = new FinancialEntryRegisteredV1(
            integrationEvent.EventId,
            integrationEvent.EntryId,
            integrationEvent.EventType,
            integrationEvent.EventVersion,
            integrationEvent.OccurredAt,
            integrationEvent.RegisteredAt,
            integrationEvent.CorrelationId,
            integrationEvent.MerchantId,
            integrationEvent.BusinessDate,
            integrationEvent.Type,
            integrationEvent.Amount,
            integrationEvent.Currency,
            integrationEvent.Description);

        return new OutboxMessage
        {
            OutboxId = Guid.NewGuid(),
            EventId = integrationEvent.EventId,
            EventType = integrationEvent.EventType,
            EventVersion = integrationEvent.EventVersion,
            Payload = JsonSerializer.Serialize(payload, EventJsonOptions),
            Status = OutboxMessageStatus.Pending,
            OccurredAt = integrationEvent.OccurredAt,
            CreatedAt = integrationEvent.RegisteredAt,
            Attempts = 0
        };
    }

    private static bool IsInputIdempotencyUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName == InputIdempotencyUniqueConstraintName;
    }
}

namespace BancoCarrefour.Consolidation.Application.ApplyFinancialEntry;

public sealed record ApplyFinancialEntryCommand(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAt,
    DateTimeOffset RegisteredAt,
    string CorrelationId,
    Guid EntryId,
    string MerchantId,
    string BusinessDate,
    string Type,
    string Amount,
    string Currency,
    string? Description);

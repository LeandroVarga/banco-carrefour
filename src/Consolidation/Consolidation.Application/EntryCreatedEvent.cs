namespace BancoCarrefour.Consolidation.Application;

public sealed record EntryCreatedEvent(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    string CorrelationId,
    Guid EntryId,
    string MerchantId,
    string BusinessDate,
    string Type,
    string Amount,
    string Currency,
    string? Description);

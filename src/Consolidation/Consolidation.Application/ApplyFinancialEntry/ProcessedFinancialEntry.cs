namespace BancoCarrefour.Consolidation.Application.ApplyFinancialEntry;

public sealed record ProcessedFinancialEntry(
    Guid EventId,
    string EventType,
    int EventVersion,
    string MerchantId,
    DateOnly BusinessDate,
    DateTimeOffset ProcessedAt);

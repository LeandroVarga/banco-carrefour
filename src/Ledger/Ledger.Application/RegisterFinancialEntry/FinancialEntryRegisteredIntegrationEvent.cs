namespace BancoCarrefour.Ledger.Application.RegisterFinancialEntry;

public sealed record FinancialEntryRegisteredIntegrationEvent(
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
    string? Description)
{
    public const string TypeName = "FinancialEntryRegistered";
    public const int Version = 1;
}

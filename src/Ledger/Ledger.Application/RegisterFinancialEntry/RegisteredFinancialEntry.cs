namespace BancoCarrefour.Ledger.Application.RegisterFinancialEntry;

public sealed record RegisteredFinancialEntry(
    Guid EntryId,
    string MerchantId,
    string BusinessDate,
    string Type,
    string Amount,
    string Currency,
    DateTimeOffset OccurredAt,
    DateTimeOffset RegisteredAt,
    string IdempotencyKey);

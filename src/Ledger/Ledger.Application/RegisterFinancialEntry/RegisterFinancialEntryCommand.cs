namespace BancoCarrefour.Ledger.Application.RegisterFinancialEntry;

public sealed record RegisterFinancialEntryCommand(
    string MerchantId,
    string IdempotencyKey,
    string Type,
    string Amount,
    string Currency,
    DateTimeOffset OccurredAt,
    string? Description,
    string CorrelationId);

namespace BancoCarrefour.Ledger.Api.Entries;

public sealed record CreateEntryResponse(
    Guid EntryId,
    string MerchantId,
    string BusinessDate,
    string Type,
    string Amount,
    string Currency,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    string IdempotencyKey);

namespace BancoCarrefour.Ledger.Api.Entries;

public sealed record CreateEntryRequest(
    string? Type,
    string? Amount,
    string? Currency,
    DateTimeOffset? OccurredAt,
    string? Description);

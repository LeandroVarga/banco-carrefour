namespace BancoCarrefour.Consolidation.Api.DailyBalances;

public sealed record DailyBalanceResponse(
    string MerchantId,
    string BusinessDate,
    string TotalCredits,
    string TotalDebits,
    string Balance,
    string Currency,
    long EntriesCount,
    DateTimeOffset LastUpdatedAt);

namespace BancoCarrefour.Consolidation.Application.GetDailyBalance;

public sealed record DailyBalanceReadModel(
    string MerchantId,
    DateOnly BusinessDate,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal Balance,
    string Currency,
    long EntryCount,
    DateTimeOffset LastUpdatedAt);

namespace BancoCarrefour.Consolidation.Application.GetDailyBalance;

public sealed record GetDailyBalanceQuery(
    string MerchantId,
    DateOnly BusinessDate);

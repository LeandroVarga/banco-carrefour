namespace BancoCarrefour.Consolidation.Application.GetDailyBalance;

public interface IDailyBalanceReader
{
    Task<DailyBalanceReadModel?> GetAsync(
        GetDailyBalanceQuery query,
        CancellationToken cancellationToken);
}

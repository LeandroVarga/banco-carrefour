namespace BancoCarrefour.Consolidation.Application.GetDailyBalance;

public interface IGetDailyBalanceUseCase
{
    Task<DailyBalanceReadModel?> GetAsync(
        GetDailyBalanceQuery query,
        CancellationToken cancellationToken);
}

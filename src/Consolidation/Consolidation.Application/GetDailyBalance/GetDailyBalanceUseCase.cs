namespace BancoCarrefour.Consolidation.Application.GetDailyBalance;

public sealed class GetDailyBalanceUseCase(IDailyBalanceReader reader) : IGetDailyBalanceUseCase
{
    public Task<DailyBalanceReadModel?> GetAsync(
        GetDailyBalanceQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return reader.GetAsync(query, cancellationToken);
    }
}

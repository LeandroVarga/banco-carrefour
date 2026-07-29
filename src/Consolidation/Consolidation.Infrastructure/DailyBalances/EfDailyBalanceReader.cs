using BancoCarrefour.Consolidation.Application.GetDailyBalance;
using Microsoft.EntityFrameworkCore;

namespace BancoCarrefour.Consolidation.Infrastructure.DailyBalances;

public sealed class EfDailyBalanceReader(ConsolidationDbContext dbContext) : IDailyBalanceReader
{
    public async Task<DailyBalanceReadModel?> GetAsync(
        GetDailyBalanceQuery query,
        CancellationToken cancellationToken)
    {
        var dailyBalance = await dbContext.DailyBalances
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.MerchantId == query.MerchantId && x.BusinessDate == query.BusinessDate,
                cancellationToken);

        return dailyBalance is null
            ? null
            : new DailyBalanceReadModel(
                dailyBalance.MerchantId,
                dailyBalance.BusinessDate,
                dailyBalance.TotalCredits,
                dailyBalance.TotalDebits,
                dailyBalance.Balance,
                dailyBalance.Currency,
                dailyBalance.EntryCount,
                dailyBalance.LastUpdatedAt.ToUniversalTime());
    }
}

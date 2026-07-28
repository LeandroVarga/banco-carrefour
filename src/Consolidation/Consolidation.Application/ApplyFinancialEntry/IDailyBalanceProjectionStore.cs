using BancoCarrefour.Consolidation.Domain;

namespace BancoCarrefour.Consolidation.Application.ApplyFinancialEntry;

public interface IDailyBalanceProjectionStore
{
    Task<ProjectionResult> ApplyAsync(
        ProcessedFinancialEntry processedEntry,
        DailyBalanceContribution contribution,
        CancellationToken cancellationToken);
}

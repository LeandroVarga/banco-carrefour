namespace BancoCarrefour.Consolidation.Application.ApplyFinancialEntry;

public interface IApplyFinancialEntryUseCase
{
    Task<ProjectionResult> ApplyAsync(
        ApplyFinancialEntryCommand command,
        CancellationToken cancellationToken);
}

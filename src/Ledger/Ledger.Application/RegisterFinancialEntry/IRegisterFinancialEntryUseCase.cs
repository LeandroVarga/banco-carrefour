namespace BancoCarrefour.Ledger.Application.RegisterFinancialEntry;

public interface IRegisterFinancialEntryUseCase
{
    Task<RegisterFinancialEntryResult> RegisterAsync(
        RegisterFinancialEntryCommand command,
        CancellationToken cancellationToken);
}

namespace BancoCarrefour.Ledger.Application.RegisterFinancialEntry;

public interface IFinancialEntryRegistrationStore
{
    Task<RegisterFinancialEntryResult> RegisterAsync(
        FinancialEntryRegistration registration,
        CancellationToken cancellationToken);
}

namespace BancoCarrefour.Ledger.Application.RegisterFinancialEntry;

public sealed class RegisterFinancialEntryValidationException : Exception
{
    public RegisterFinancialEntryValidationException(IReadOnlyCollection<string> errors)
        : base("Registro de lançamento inválido.")
    {
        Errors = errors;
    }

    public IReadOnlyCollection<string> Errors { get; }
}

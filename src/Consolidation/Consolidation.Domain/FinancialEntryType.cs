namespace BancoCarrefour.Consolidation.Domain;

public enum FinancialEntryType
{
    Credit = 1,
    Debit = 2
}

public static class FinancialEntryTypeParser
{
    public static FinancialEntryType Parse(string? value)
    {
        return value switch
        {
            "CREDIT" => FinancialEntryType.Credit,
            "DEBIT" => FinancialEntryType.Debit,
            _ => throw new DomainValidationException("type deve ser CREDIT ou DEBIT.")
        };
    }
}

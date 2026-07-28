namespace BancoCarrefour.Ledger.Domain;

public enum EntryType
{
    Credit = 1,
    Debit = 2
}

public static class EntryTypeParser
{
    public static EntryType Parse(string? value)
    {
        return value switch
        {
            "CREDIT" => EntryType.Credit,
            "DEBIT" => EntryType.Debit,
            _ => throw new DomainValidationException("type deve ser CREDIT ou DEBIT.")
        };
    }

    public static string ToContractValue(this EntryType value)
    {
        return value switch
        {
            EntryType.Credit => "CREDIT",
            EntryType.Debit => "DEBIT",
            _ => throw new DomainValidationException("type deve ser CREDIT ou DEBIT.")
        };
    }
}

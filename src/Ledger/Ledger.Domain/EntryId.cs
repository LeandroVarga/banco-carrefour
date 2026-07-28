namespace BancoCarrefour.Ledger.Domain;

public readonly record struct EntryId(Guid Value)
{
    public static EntryId New() => new(Guid.NewGuid());

    public static EntryId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("entryId é obrigatório.");
        }

        return new EntryId(value);
    }
}

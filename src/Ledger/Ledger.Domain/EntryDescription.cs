namespace BancoCarrefour.Ledger.Domain;

public readonly record struct EntryDescription
{
    public const int MaxLength = 256;

    private EntryDescription(string? value)
    {
        Value = value;
    }

    public string? Value { get; }

    public static EntryDescription Create(string? value)
    {
        if (value is null)
        {
            return new EntryDescription(null);
        }

        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return new EntryDescription(null);
        }

        if (normalized.Length > MaxLength)
        {
            throw new DomainValidationException("description deve ter no máximo 256 caracteres.");
        }

        return new EntryDescription(normalized);
    }
}

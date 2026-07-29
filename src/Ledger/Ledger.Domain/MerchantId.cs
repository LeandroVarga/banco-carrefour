namespace BancoCarrefour.Ledger.Domain;

public readonly record struct MerchantId
{
    public const int MaxLength = 64;

    private MerchantId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static MerchantId Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("merchantId é obrigatório.");
        }

        var normalized = value.Trim();
        if (normalized.Length > MaxLength)
        {
            throw new DomainValidationException("merchantId deve ter no máximo 64 caracteres.");
        }

        return new MerchantId(normalized);
    }
}

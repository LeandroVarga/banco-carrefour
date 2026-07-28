namespace BancoCarrefour.Consolidation.Domain;

public readonly record struct MerchantId
{
    private MerchantId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static MerchantId Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            throw new DomainValidationException("merchantId é obrigatório e deve ter no máximo 64 caracteres.");
        }

        return new MerchantId(value);
    }
}

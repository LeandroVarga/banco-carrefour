namespace BancoCarrefour.Consolidation.Domain;

public readonly record struct BusinessDate
{
    private BusinessDate(DateOnly value)
    {
        Value = value;
    }

    public DateOnly Value { get; }

    public static BusinessDate Create(DateOnly value)
    {
        if (value == default)
        {
            throw new DomainValidationException("businessDate é obrigatório.");
        }

        return new BusinessDate(value);
    }
}

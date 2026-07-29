using System.Globalization;

namespace BancoCarrefour.Consolidation.Domain;

public readonly record struct Money
{
    public const string SupportedCurrency = "BRL";

    private Money(decimal amount)
    {
        Amount = amount;
    }

    public decimal Amount { get; }

    public static Money CreatePositive(decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainValidationException("amount deve ser monetário positivo conforme contrato.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainValidationException("amount deve ter no máximo duas casas decimais.");
        }

        return new Money(amount);
    }

    public static string Format(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

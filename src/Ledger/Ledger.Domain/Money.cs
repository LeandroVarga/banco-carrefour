using System.Globalization;

namespace BancoCarrefour.Ledger.Domain;

public readonly record struct Money
{
    public const string SupportedCurrency = "BRL";

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Create(decimal amount, string? currency)
    {
        if (currency != SupportedCurrency)
        {
            throw new DomainValidationException("currency deve ser BRL.");
        }

        if (amount <= 0)
        {
            throw new DomainValidationException("amount deve ser monetário positivo conforme contrato.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainValidationException("amount deve ter no máximo duas casas decimais.");
        }

        return new Money(amount, SupportedCurrency);
    }

    public string Format()
    {
        return Amount.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

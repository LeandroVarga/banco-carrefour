namespace BancoCarrefour.Consolidation.Domain;

public sealed record DailyBalanceContribution(
    MerchantId MerchantId,
    BusinessDate BusinessDate,
    FinancialEntryType Type,
    Money Amount,
    DateTimeOffset OccurredAt)
{
    public decimal CreditAmount => Type == FinancialEntryType.Credit ? Amount.Amount : 0m;

    public decimal DebitAmount => Type == FinancialEntryType.Debit ? Amount.Amount : 0m;

    public decimal BalanceAmount => Type == FinancialEntryType.Credit ? Amount.Amount : -Amount.Amount;
}

namespace BancoCarrefour.Consolidation.Domain;

public sealed class DailyBalance
{
    private DailyBalance(
        MerchantId merchantId,
        BusinessDate businessDate,
        decimal totalCredits,
        decimal totalDebits,
        long entryCount,
        DateTimeOffset lastEventOccurredAt)
    {
        MerchantId = merchantId;
        BusinessDate = businessDate;
        TotalCredits = totalCredits;
        TotalDebits = totalDebits;
        EntryCount = entryCount;
        LastEventOccurredAt = lastEventOccurredAt.ToUniversalTime();
    }

    public MerchantId MerchantId { get; }

    public BusinessDate BusinessDate { get; }

    public decimal TotalCredits { get; }

    public decimal TotalDebits { get; }

    public decimal Balance => TotalCredits - TotalDebits;

    public long EntryCount { get; }

    public DateTimeOffset LastEventOccurredAt { get; }

    public static DailyBalance Start(DailyBalanceContribution contribution)
    {
        return new DailyBalance(
            contribution.MerchantId,
            contribution.BusinessDate,
            contribution.CreditAmount,
            contribution.DebitAmount,
            1,
            contribution.OccurredAt);
    }

    public DailyBalance Apply(DailyBalanceContribution contribution)
    {
        if (contribution.MerchantId != MerchantId || contribution.BusinessDate != BusinessDate)
        {
            throw new DomainValidationException("contribuição pertence a outro comerciante ou data de negócio.");
        }

        return new DailyBalance(
            MerchantId,
            BusinessDate,
            TotalCredits + contribution.CreditAmount,
            TotalDebits + contribution.DebitAmount,
            EntryCount + 1,
            contribution.OccurredAt > LastEventOccurredAt ? contribution.OccurredAt : LastEventOccurredAt);
    }
}

namespace BancoCarrefour.Ledger.Domain;

public sealed class FinancialEntry
{
    private FinancialEntry(
        EntryId entryId,
        MerchantId merchantId,
        BusinessDate businessDate,
        EntryType type,
        Money money,
        DateTimeOffset occurredAt,
        DateTimeOffset registeredAt,
        EntryDescription description)
    {
        EntryId = entryId;
        MerchantId = merchantId;
        BusinessDate = businessDate;
        Type = type;
        Money = money;
        OccurredAt = occurredAt.ToUniversalTime();
        RegisteredAt = registeredAt.ToUniversalTime();
        Description = description;
    }

    public EntryId EntryId { get; }

    public MerchantId MerchantId { get; }

    public BusinessDate BusinessDate { get; }

    public EntryType Type { get; }

    public Money Money { get; }

    public DateTimeOffset OccurredAt { get; }

    public DateTimeOffset RegisteredAt { get; }

    public EntryDescription Description { get; }

    public static FinancialEntry Register(
        EntryId entryId,
        MerchantId merchantId,
        EntryType type,
        Money money,
        DateTimeOffset occurredAt,
        DateTimeOffset registeredAt,
        EntryDescription description)
    {
        if (registeredAt == default)
        {
            throw new DomainValidationException("registeredAt é obrigatório.");
        }

        return new FinancialEntry(
            entryId,
            merchantId,
            BusinessDate.FromOccurredAt(occurredAt),
            type,
            money,
            occurredAt,
            registeredAt,
            description);
    }
}

namespace BancoCarrefour.Ledger.Persistence.Entities;

public sealed class Entry
{
    public Guid EntryId { get; set; }

    public string MerchantId { get; set; } = string.Empty;

    public DateOnly BusinessDate { get; set; }

    public EntryType Type { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? Description { get; set; }
}

namespace BancoCarrefour.Consolidation.Persistence.Entities;

public sealed class DailyBalance
{
    public Guid DailyBalanceId { get; set; }

    public string MerchantId { get; set; } = string.Empty;

    public DateOnly BusinessDate { get; set; }

    public decimal TotalCredits { get; set; }

    public decimal TotalDebits { get; set; }

    public decimal Balance { get; set; }

    public string Currency { get; set; } = string.Empty;

    public long EntryCount { get; set; }

    public DateTimeOffset LastEventOccurredAt { get; set; }

    public DateTimeOffset LastUpdatedAt { get; set; }
}

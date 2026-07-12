namespace BancoCarrefour.Consolidation.Persistence.Entities;

public sealed class ProcessedEvent
{
    public Guid ProcessedEventId { get; set; }

    public Guid EventId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public int EventVersion { get; set; }

    public string MerchantId { get; set; } = string.Empty;

    public DateOnly BusinessDate { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}

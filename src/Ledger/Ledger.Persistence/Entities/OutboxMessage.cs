namespace BancoCarrefour.Ledger.Persistence.Entities;

public sealed class OutboxMessage
{
    public Guid OutboxId { get; set; }

    public Guid EventId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public int EventVersion { get; set; }

    public string Payload { get; set; } = string.Empty;

    public OutboxMessageStatus Status { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? LockedAt { get; set; }
}

namespace BancoCarrefour.Ledger.Application.PublishOutbox;

public sealed record OutboxEvent(
    Guid OutboxId,
    Guid EventId,
    string EventType,
    int EventVersion,
    string Payload,
    int Attempts,
    DateTimeOffset CreatedAt);

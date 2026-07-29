namespace BancoCarrefour.Ledger.Application.PublishOutbox;

public sealed record OutboxPublishResult(
    int Claimed,
    int Published,
    int Failed);

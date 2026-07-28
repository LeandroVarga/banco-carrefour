namespace BancoCarrefour.Ledger.Application.PublishOutbox;

public sealed record OutboxFailure(
    Guid OutboxId,
    string Error,
    DateTimeOffset NextAttemptAt);

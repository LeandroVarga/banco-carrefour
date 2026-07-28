namespace BancoCarrefour.Ledger.Application.PublishOutbox;

public sealed class OutboxPublishingOptions
{
    public const string SectionName = "OutboxPublisher";

    public int BatchSize { get; set; } = 10;

    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
}

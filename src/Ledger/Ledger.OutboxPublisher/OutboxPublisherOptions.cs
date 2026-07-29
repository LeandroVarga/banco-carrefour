namespace BancoCarrefour.Ledger.OutboxPublisher;

public sealed class OutboxPublisherOptions
{
    public const string SectionName = "OutboxPublisher";

    public int BatchSize { get; set; } = 20;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
}

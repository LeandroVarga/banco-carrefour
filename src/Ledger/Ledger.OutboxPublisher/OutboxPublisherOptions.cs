namespace BancoCarrefour.Ledger.OutboxPublisher;

public sealed class OutboxPublisherOptions
{
    public const string SectionName = "OutboxPublisher";

    public int BatchSize { get; set; } = 20;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);
}

namespace BancoCarrefour.Consolidation.Worker;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "rabbitmq";

    public int Port { get; set; } = 5672;

    public string UserName { get; set; } = "ledger";

    public string Password { get; set; } = "ledger";

    public string ExchangeName { get; set; } = "ledger.events";

    public string ExchangeType { get; set; } = "topic";

    public string QueueName { get; set; } = "consolidation.entry-created";

    public string RoutingKey { get; set; } = "ledger.entry.created.v1";

    public ushort PrefetchCount { get; set; } = 1;
}

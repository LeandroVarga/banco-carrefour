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

    public string DeadLetterExchangeName { get; set; } = "consolidation.dlx";

    public string DeadLetterExchangeType { get; set; } = "direct";

    public string DeadLetterQueueName { get; set; } = "consolidation.entry-created.dlq";

    public string DeadLetterRoutingKey { get; set; } = "consolidation.entry-created.dead";

    public string RetryExchangeName { get; set; } = "consolidation.retry";

    public string RetryExchangeType { get; set; } = "direct";

    public string RetryQueueName { get; set; } = "consolidation.entry-created.retry";

    public string RetryRoutingKey { get; set; } = "consolidation.entry-created.retry";

    public int RetryDelayMilliseconds { get; set; } = 5000;

    public int MaxRetryAttempts { get; set; } = 3;

    public ushort PrefetchCount { get; set; } = 1;
}

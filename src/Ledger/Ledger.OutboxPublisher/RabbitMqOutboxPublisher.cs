using BancoCarrefour.Ledger.Persistence.Entities;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace BancoCarrefour.Ledger.OutboxPublisher;

public sealed class RabbitMqOutboxPublisher(IOptions<RabbitMqOptions> options) : IOutboxPublisher
{
    private readonly RabbitMqOptions options = options.Value;

    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var document = JsonDocument.Parse(message.Payload);
        var root = document.RootElement;
        var eventId = root.GetProperty("eventId").GetString();
        var correlationId = root.GetProperty("correlationId").GetString();
        var eventType = root.GetProperty("eventType").GetString();
        var eventVersion = root.GetProperty("eventVersion").GetInt32();

        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password,
            DispatchConsumersAsync = true
        };

        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare(
            exchange: options.ExchangeName,
            type: options.ExchangeType,
            durable: true,
            autoDelete: false);
        channel.ConfirmSelect();

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.MessageId = eventId;
        properties.CorrelationId = correlationId;
        properties.Type = eventType;
        properties.Headers = new Dictionary<string, object>
        {
            ["eventType"] = eventType ?? string.Empty,
            ["eventVersion"] = eventVersion
        };

        var body = Encoding.UTF8.GetBytes(message.Payload);

        channel.BasicPublish(
            exchange: options.ExchangeName,
            routingKey: options.RoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body);
        channel.WaitForConfirmsOrDie(options.PublishConfirmTimeout);

        return Task.CompletedTask;
    }
}

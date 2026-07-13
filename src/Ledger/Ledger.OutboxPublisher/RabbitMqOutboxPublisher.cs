using BancoCarrefour.Ledger.Persistence.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace BancoCarrefour.Ledger.OutboxPublisher;

public sealed class RabbitMqOutboxPublisher(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqOutboxPublisher> logger) : IOutboxPublisher
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

        using var returnReceived = new ManualResetEventSlim(false);
        var messageReturned = false;
        ushort replyCode = 0;
        string? replyText = null;
        channel.BasicReturn += (_, args) =>
        {
            messageReturned = true;
            replyCode = args.ReplyCode;
            replyText = args.ReplyText;
            returnReceived.Set();
        };

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

        logger.LogInformation(
            "Publicando mensagem da Outbox no RabbitMQ. OutboxId={OutboxId}; EventId={EventId}; EventType={EventType}; CorrelationId={CorrelationId}; Exchange={Exchange}; RoutingKey={RoutingKey}",
            message.OutboxId,
            eventId,
            eventType,
            correlationId,
            options.ExchangeName,
            options.RoutingKey);

        channel.BasicPublish(
            exchange: options.ExchangeName,
            routingKey: options.RoutingKey,
            mandatory: true,
            basicProperties: properties,
            body: body);
        channel.WaitForConfirmsOrDie(options.PublishConfirmTimeout);

        if (!messageReturned)
        {
            returnReceived.Wait(options.MandatoryReturnTimeout);
        }

        if (messageReturned)
        {
            Observability.MessagesUnroutable.Add(1);
            logger.LogWarning(
                "Mensagem da Outbox não foi roteada pelo RabbitMQ. OutboxId={OutboxId}; EventId={EventId}; EventType={EventType}; CorrelationId={CorrelationId}; ReplyCode={ReplyCode}; ReplyText={ReplyText}",
                message.OutboxId,
                eventId,
                eventType,
                correlationId,
                replyCode,
                replyText);

            throw new InvalidOperationException(
                $"Mensagem da Outbox não foi roteada pelo RabbitMQ. ReplyCode={replyCode}; ReplyText={replyText}");
        }

        logger.LogInformation(
            "Publicação RabbitMQ confirmada. OutboxId={OutboxId}; EventId={EventId}; EventType={EventType}; CorrelationId={CorrelationId}",
            message.OutboxId,
            eventId,
            eventType,
            correlationId);

        return Task.CompletedTask;
    }
}

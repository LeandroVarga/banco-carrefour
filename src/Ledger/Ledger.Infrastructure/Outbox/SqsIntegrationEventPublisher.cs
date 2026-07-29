using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using BancoCarrefour.Ledger.Application.PublishOutbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BancoCarrefour.Ledger.Infrastructure.Outbox;

public sealed class SqsIntegrationEventPublisher(
    IAmazonSQS sqs,
    IOptions<SqsOptions> options,
    ILogger<SqsIntegrationEventPublisher> logger) : IIntegrationEventPublisher
{
    public async Task PublishAsync(
        OutboxEvent outboxEvent,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(outboxEvent.Payload);
        var root = document.RootElement;
        var correlationId = root.TryGetProperty("correlationId", out var correlationElement)
            ? correlationElement.GetString()
            : null;

        var request = new SendMessageRequest
        {
            QueueUrl = options.Value.QueueUrl,
            MessageBody = outboxEvent.Payload,
            MessageAttributes =
            {
                ["eventId"] = new MessageAttributeValue { DataType = "String", StringValue = outboxEvent.EventId.ToString() },
                ["eventType"] = new MessageAttributeValue { DataType = "String", StringValue = outboxEvent.EventType },
                ["eventVersion"] = new MessageAttributeValue { DataType = "Number", StringValue = outboxEvent.EventVersion.ToString() },
                ["correlationId"] = new MessageAttributeValue { DataType = "String", StringValue = correlationId ?? string.Empty }
            }
        };

        logger.LogInformation(
            "Publicando evento da Outbox no SQS. OutboxId={OutboxId}; EventId={EventId}; EventType={EventType}; CorrelationId={CorrelationId}; QueueUrl={QueueUrl}",
            outboxEvent.OutboxId,
            outboxEvent.EventId,
            outboxEvent.EventType,
            correlationId,
            options.Value.QueueUrl);

        var response = await sqs.SendMessageAsync(request, cancellationToken);

        if (string.IsNullOrWhiteSpace(response.MessageId))
        {
            throw new InvalidOperationException("SQS não retornou MessageId para a mensagem publicada.");
        }
    }
}

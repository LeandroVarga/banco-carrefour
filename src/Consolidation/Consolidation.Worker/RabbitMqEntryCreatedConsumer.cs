using BancoCarrefour.Consolidation.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text.Json;

namespace BancoCarrefour.Consolidation.Worker;

public sealed class RabbitMqEntryCreatedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqEntryCreatedConsumer> logger) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RabbitMqOptions options = options.Value;
    private IConnection? connection;
    private IModel? channel;

    public void Start(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password,
            DispatchConsumersAsync = true
        };

        connection = factory.CreateConnection();
        channel = connection.CreateModel();

        channel.ExchangeDeclare(
            exchange: options.ExchangeName,
            type: options.ExchangeType,
            durable: true,
            autoDelete: false);

        channel.ExchangeDeclare(
            exchange: options.DeadLetterExchangeName,
            type: options.DeadLetterExchangeType,
            durable: true,
            autoDelete: false);

        channel.QueueDeclare(
            queue: options.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false);

        channel.QueueBind(
            queue: options.DeadLetterQueueName,
            exchange: options.DeadLetterExchangeName,
            routingKey: options.DeadLetterRoutingKey);

        channel.QueueDeclare(
            queue: options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false);

        channel.QueueBind(
            queue: options.QueueName,
            exchange: options.ExchangeName,
            routingKey: options.RoutingKey);

        channel.BasicQos(
            prefetchSize: 0,
            prefetchCount: options.PrefetchCount,
            global: false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, args) =>
        {
            await ProcessDeliveryAsync(args.Body, args.DeliveryTag, channel, cancellationToken);
        };

        channel.BasicConsume(
            queue: options.QueueName,
            autoAck: false,
            consumer: consumer);
    }

    public async Task ProcessDeliveryAsync(
        ReadOnlyMemory<byte> body,
        ulong deliveryTag,
        IModel channel,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = JsonSerializer.Deserialize<EntryCreatedEvent>(body.Span, JsonOptions)
                ?? throw new ProjectionValidationException("Mensagem EntryCreated.v1 vazia.");

            using var scope = scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<EntryCreatedProjectionProcessor>();

            var result = await processor.ProcessAsync(message, cancellationToken);

            channel.BasicAck(deliveryTag, multiple: false);
            logger.LogInformation(
                "Evento EntryCreated processado. EventId={EventId}; Applied={Applied}; Duplicate={Duplicate}",
                message.EventId,
                result.Applied,
                result.Duplicate);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            channel.BasicNack(deliveryTag, multiple: false, requeue: true);
        }
        catch (ProjectionValidationException exception)
        {
            if (TryPublishToDeadLetter(channel, body, "projection-validation-error", exception))
            {
                channel.BasicAck(deliveryTag, multiple: false);
                logger.LogWarning(exception, "Mensagem EntryCreated.v1 inválida foi encaminhada para a DLQ.");
                return;
            }

            channel.BasicNack(deliveryTag, multiple: false, requeue: true);
        }
        catch (JsonException exception)
        {
            if (TryPublishToDeadLetter(channel, body, "json-deserialization-error", exception))
            {
                channel.BasicAck(deliveryTag, multiple: false);
                logger.LogWarning(exception, "Mensagem EntryCreated.v1 não pôde ser desserializada e foi encaminhada para a DLQ.");
                return;
            }

            channel.BasicNack(deliveryTag, multiple: false, requeue: true);
        }
        catch (Exception exception)
        {
            channel.BasicNack(deliveryTag, multiple: false, requeue: true);
            logger.LogError(exception, "Falha transitória ao processar mensagem EntryCreated.v1.");
        }
    }

    private bool TryPublishToDeadLetter(
        IModel channel,
        ReadOnlyMemory<byte> body,
        string reason,
        Exception exception)
    {
        try
        {
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            properties.Headers = new Dictionary<string, object>
            {
                ["x-dead-letter-reason"] = reason,
                ["x-dead-letter-source"] = options.QueueName,
                ["x-dead-letter-exception"] = exception.GetType().Name
            };

            channel.BasicPublish(
                exchange: options.DeadLetterExchangeName,
                routingKey: options.DeadLetterRoutingKey,
                basicProperties: properties,
                body: body);

            return true;
        }
        catch (Exception publishException)
        {
            logger.LogError(
                publishException,
                "Falha ao encaminhar mensagem EntryCreated.v1 para DLQ. DeadLetterExchange={DeadLetterExchange}; DeadLetterQueue={DeadLetterQueue}",
                options.DeadLetterExchangeName,
                options.DeadLetterQueueName);

            return false;
        }
    }

    public void Dispose()
    {
        channel?.Dispose();
        connection?.Dispose();
    }
}

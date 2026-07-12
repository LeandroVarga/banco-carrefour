using BancoCarrefour.Consolidation.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Globalization;
using System.Text;
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

        channel.ExchangeDeclare(
            exchange: options.RetryExchangeName,
            type: options.RetryExchangeType,
            durable: true,
            autoDelete: false);

        channel.QueueDeclare(
            queue: options.RetryQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                ["x-message-ttl"] = Math.Max(0, options.RetryDelayMilliseconds),
                ["x-dead-letter-exchange"] = options.ExchangeName,
                ["x-dead-letter-routing-key"] = options.RoutingKey
            });

        channel.QueueBind(
            queue: options.RetryQueueName,
            exchange: options.RetryExchangeName,
            routingKey: options.RetryRoutingKey);

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
            await ProcessDeliveryAsync(args.Body, args.BasicProperties, args.DeliveryTag, channel, cancellationToken);
        };

        channel.BasicConsume(
            queue: options.QueueName,
            autoAck: false,
            consumer: consumer);
    }

    public async Task ProcessDeliveryAsync(
        ReadOnlyMemory<byte> body,
        IBasicProperties? basicProperties,
        ulong deliveryTag,
        IModel channel,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var retryCount = GetRetryCount(basicProperties);
        var headerCorrelationId = GetCorrelationId(basicProperties);
        var deliveryCorrelationId = headerCorrelationId;
        using var consumeActivity = Observability.ActivitySource.StartActivity("consolidation.event.consume");

        Observability.EventsConsumed.Add(1);
        consumeActivity?.SetTag("correlation.id", headerCorrelationId);
        consumeActivity?.SetTag("message.retry_count", retryCount);

        try
        {
            var message = JsonSerializer.Deserialize<EntryCreatedEvent>(body.Span, JsonOptions)
                ?? throw new ProjectionValidationException("Mensagem EntryCreated.v1 vazia.");

            var correlationId = string.IsNullOrWhiteSpace(message.CorrelationId)
                ? headerCorrelationId
                : message.CorrelationId;
            deliveryCorrelationId = correlationId;

            consumeActivity?.SetTag("correlation.id", correlationId);
            consumeActivity?.SetTag("event.id", message.EventId);
            consumeActivity?.SetTag("entry.id", message.EntryId);
            consumeActivity?.SetTag("merchant.id", message.MerchantId);
            consumeActivity?.SetTag("business.date", message.BusinessDate);

            logger.LogInformation(
                "Mensagem EntryCreated recebida. EventId={EventId}; EntryId={EntryId}; MerchantId={MerchantId}; BusinessDate={BusinessDate}; CorrelationId={CorrelationId}; RetryCount={RetryCount}",
                message.EventId,
                message.EntryId,
                message.MerchantId,
                message.BusinessDate,
                correlationId,
                retryCount);

            using var scope = scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IEntryCreatedProjectionProcessor>();

            using var processActivity = Observability.ActivitySource.StartActivity("consolidation.event.process");
            processActivity?.SetTag("correlation.id", correlationId);
            processActivity?.SetTag("event.id", message.EventId);
            processActivity?.SetTag("merchant.id", message.MerchantId);
            processActivity?.SetTag("business.date", message.BusinessDate);

            var result = await processor.ProcessAsync(message, cancellationToken);

            channel.BasicAck(deliveryTag, multiple: false);
            consumeActivity?.SetStatus(ActivityStatusCode.Ok);
            processActivity?.SetStatus(ActivityStatusCode.Ok);

            if (result.Duplicate)
            {
                Observability.EventsDuplicated.Add(1);
                logger.LogInformation(
                    "Evento EntryCreated duplicado descartado. EventId={EventId}; CorrelationId={CorrelationId}",
                    message.EventId,
                    correlationId);
            }
            else
            {
                Observability.EventsProcessed.Add(1);
                logger.LogInformation(
                    "Evento EntryCreated aplicado. EventId={EventId}; MerchantId={MerchantId}; BusinessDate={BusinessDate}; CorrelationId={CorrelationId}",
                    message.EventId,
                    message.MerchantId,
                    result.BusinessDate,
                    correlationId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            channel.BasicNack(deliveryTag, multiple: false, requeue: true);
        }
        catch (ProjectionValidationException exception)
        {
            if (TryPublishToDeadLetter(channel, basicProperties, body, "projection-validation-error", exception))
            {
                channel.BasicAck(deliveryTag, multiple: false);
                Observability.EventsInvalid.Add(1);
                Observability.EventsDeadlettered.Add(1);
                consumeActivity?.SetStatus(ActivityStatusCode.Error, "projection validation error");
                logger.LogWarning(
                    exception,
                    "Mensagem EntryCreated.v1 inválida foi encaminhada para a DLQ. CorrelationId={CorrelationId}; RetryCount={RetryCount}",
                    deliveryCorrelationId,
                    retryCount);
                return;
            }

            channel.BasicNack(deliveryTag, multiple: false, requeue: true);
        }
        catch (JsonException exception)
        {
            if (TryPublishToDeadLetter(channel, basicProperties, body, "json-deserialization-error", exception))
            {
                channel.BasicAck(deliveryTag, multiple: false);
                Observability.EventsInvalid.Add(1);
                Observability.EventsDeadlettered.Add(1);
                consumeActivity?.SetStatus(ActivityStatusCode.Error, "json deserialization error");
                logger.LogWarning(
                    exception,
                    "Mensagem EntryCreated.v1 não pôde ser desserializada e foi encaminhada para a DLQ. CorrelationId={CorrelationId}; RetryCount={RetryCount}",
                    deliveryCorrelationId,
                    retryCount);
                return;
            }

            channel.BasicNack(deliveryTag, multiple: false, requeue: true);
        }
        catch (Exception exception)
        {
            var maxRetryAttempts = Math.Max(0, options.MaxRetryAttempts);
            Observability.EventsProcessingFailed.Add(1);

            if (retryCount < maxRetryAttempts)
            {
                var nextRetryCount = retryCount + 1;

                if (TryPublishToRetry(channel, basicProperties, body, nextRetryCount, exception))
                {
                    channel.BasicAck(deliveryTag, multiple: false);
                    Observability.EventsRetried.Add(1);
                    consumeActivity?.SetStatus(ActivityStatusCode.Error, "event sent to retry");
                    logger.LogWarning(
                        exception,
                        "Falha transitória ao processar EntryCreated.v1. Mensagem encaminhada para retry. CorrelationId={CorrelationId}; RetryCount={RetryCount}; MaxRetryAttempts={MaxRetryAttempts}",
                        deliveryCorrelationId,
                        nextRetryCount,
                        maxRetryAttempts);
                    return;
                }

                channel.BasicNack(deliveryTag, multiple: false, requeue: true);
                return;
            }

            if (TryPublishToDeadLetter(channel, basicProperties, body, "retry-attempts-exhausted", exception))
            {
                channel.BasicAck(deliveryTag, multiple: false);
                Observability.EventsDeadlettered.Add(1);
                consumeActivity?.SetStatus(ActivityStatusCode.Error, "retry attempts exhausted");
                logger.LogError(
                    exception,
                    "Falha transitória excedeu limite de retries e foi encaminhada para DLQ. CorrelationId={CorrelationId}; RetryCount={RetryCount}; MaxRetryAttempts={MaxRetryAttempts}",
                    deliveryCorrelationId,
                    retryCount,
                    maxRetryAttempts);
                return;
            }

            channel.BasicNack(deliveryTag, multiple: false, requeue: true);
        }
        finally
        {
            Observability.EventProcessDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private bool TryPublishToRetry(
        IModel channel,
        IBasicProperties? basicProperties,
        ReadOnlyMemory<byte> body,
        int retryCount,
        Exception exception)
    {
        try
        {
            using var activity = Observability.ActivitySource.StartActivity("consolidation.event.retry");
            activity?.SetTag("correlation.id", GetCorrelationId(basicProperties));
            activity?.SetTag("message.retry_count", retryCount);
            activity?.SetTag("message.destination", options.RetryQueueName);

            var properties = CreateForwardedProperties(channel, basicProperties);
            properties.Headers = CopyHeaders(basicProperties);
            properties.Headers["x-retry-count"] = retryCount;
            properties.Headers["x-retry-source"] = options.QueueName;
            properties.Headers["x-retry-exception"] = exception.GetType().Name;

            return PublishConfirmedAndRouted(
                channel,
                options.RetryExchangeName,
                options.RetryRoutingKey,
                options.RetryQueueName,
                properties,
                body);
        }
        catch (Exception publishException)
        {
            logger.LogError(
                publishException,
                "Falha ao encaminhar mensagem EntryCreated.v1 para retry. RetryExchange={RetryExchange}; RetryQueue={RetryQueue}",
                options.RetryExchangeName,
                options.RetryQueueName);

            return false;
        }
    }

    private bool TryPublishToDeadLetter(
        IModel channel,
        IBasicProperties? basicProperties,
        ReadOnlyMemory<byte> body,
        string reason,
        Exception exception)
    {
        try
        {
            using var activity = Observability.ActivitySource.StartActivity("consolidation.event.deadletter");
            activity?.SetTag("correlation.id", GetCorrelationId(basicProperties));
            activity?.SetTag("message.destination", options.DeadLetterQueueName);
            activity?.SetTag("message.deadletter_reason", reason);

            var properties = CreateForwardedProperties(channel, basicProperties);
            properties.Headers = CopyHeaders(basicProperties);
            properties.Headers["x-dead-letter-reason"] = reason;
            properties.Headers["x-dead-letter-source"] = options.QueueName;
            properties.Headers["x-dead-letter-exception"] = exception.GetType().Name;

            return PublishConfirmedAndRouted(
                channel,
                options.DeadLetterExchangeName,
                options.DeadLetterRoutingKey,
                options.DeadLetterQueueName,
                properties,
                body);
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

    private static IBasicProperties CreateForwardedProperties(
        IModel channel,
        IBasicProperties? sourceProperties)
    {
        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = string.IsNullOrWhiteSpace(sourceProperties?.ContentType)
            ? "application/json"
            : sourceProperties.ContentType;
        properties.CorrelationId = sourceProperties?.CorrelationId;
        properties.MessageId = sourceProperties?.MessageId;
        properties.Type = sourceProperties?.Type;
        properties.AppId = sourceProperties?.AppId;

        return properties;
    }

    private bool PublishConfirmedAndRouted(
        IModel channel,
        string exchange,
        string routingKey,
        string destination,
        IBasicProperties properties,
        ReadOnlyMemory<byte> body)
    {
        var returned = false;
        ushort? returnReplyCode = null;
        string? returnReplyText = null;

        void OnBasicReturn(object? _, BasicReturnEventArgs args)
        {
            if (!string.Equals(args.Exchange, exchange, StringComparison.Ordinal)
                || !string.Equals(args.RoutingKey, routingKey, StringComparison.Ordinal))
            {
                return;
            }

            returned = true;
            returnReplyCode = args.ReplyCode;
            returnReplyText = args.ReplyText;
        }

        channel.BasicReturn += OnBasicReturn;

        try
        {
            channel.ConfirmSelect();
            channel.BasicPublish(
                exchange: exchange,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body);
            channel.WaitForConfirmsOrDie(TimeSpan.FromMilliseconds(Math.Max(1, options.PublishConfirmTimeoutMilliseconds)));

            if (!returned)
            {
                return true;
            }

            logger.LogError(
                "Publicação de mensagem EntryCreated.v1 não foi roteada. Exchange={Exchange}; RoutingKey={RoutingKey}; Destination={Destination}; ReplyCode={ReplyCode}; ReplyText={ReplyText}",
                exchange,
                routingKey,
                destination,
                returnReplyCode,
                returnReplyText);

            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Publicação de mensagem EntryCreated.v1 não foi confirmada pelo broker. Exchange={Exchange}; RoutingKey={RoutingKey}; Destination={Destination}",
                exchange,
                routingKey,
                destination);

            return false;
        }
        finally
        {
            channel.BasicReturn -= OnBasicReturn;
        }
    }

    private static IDictionary<string, object> CopyHeaders(IBasicProperties? basicProperties)
    {
        return basicProperties?.Headers is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(basicProperties.Headers);
    }

    private static int GetRetryCount(IBasicProperties? basicProperties)
    {
        if (basicProperties?.Headers is null
            || !basicProperties.Headers.TryGetValue("x-retry-count", out var value))
        {
            return 0;
        }

        return value switch
        {
            byte retryCount => retryCount,
            short retryCount => Math.Max(0, (int)retryCount),
            int retryCount => Math.Max(0, retryCount),
            long retryCount when retryCount <= int.MaxValue => Math.Max(0, (int)retryCount),
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), NumberStyles.Integer, CultureInfo.InvariantCulture, out var retryCount) => Math.Max(0, retryCount),
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var retryCount) => Math.Max(0, retryCount),
            _ => 0
        };
    }

    private static string? GetCorrelationId(IBasicProperties? basicProperties)
    {
        return string.IsNullOrWhiteSpace(basicProperties?.CorrelationId)
            ? null
            : basicProperties.CorrelationId;
    }

    public void Dispose()
    {
        channel?.Dispose();
        connection?.Dispose();
    }
}

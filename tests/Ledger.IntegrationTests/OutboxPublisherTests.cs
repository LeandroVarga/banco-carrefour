using BancoCarrefour.Ledger.OutboxPublisher;
using BancoCarrefour.Ledger.Persistence;
using BancoCarrefour.Ledger.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Xunit;

namespace BancoCarrefour.Ledger.IntegrationTests;

public sealed class OutboxPublisherTests : IClassFixture<LedgerApiFactory>, IAsyncLifetime
{
    private const string DefaultExchangeName = "ledger.events";
    private const string DefaultRoutingKey = "ledger.entry.created.v1";
    private readonly LedgerApiFactory factory;

    public OutboxPublisherTests(LedgerApiFactory factory)
    {
        this.factory = factory;
    }

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PublishPendingAsync_deve_publicar_payload_persistido_e_marcar_outbox_como_published()
    {
        var exchangeName = $"ledger.events.test.{Guid.NewGuid():N}";
        var routingKey = $"ledger.entry.created.v1.{Guid.NewGuid():N}";
        var queueName = $"ledger-outbox-test-{Guid.NewGuid():N}";
        var eventId = Guid.NewGuid();
        var correlationId = "corr-outbox-test";
        var payload = CreateEntryCreatedPayload(eventId, correlationId);

        await InsertPendingOutboxMessageAsync(eventId, payload);
        DeclareTestQueue(exchangeName, routingKey, queueName);

        try
        {
            var processor = CreateProcessor(new RabbitMqOptions
            {
                ExchangeName = exchangeName,
                RoutingKey = routingKey
            });

            var published = await processor.PublishPendingAsync(CancellationToken.None);
            var result = await GetMessageAsync(queueName);
            var outbox = await GetOutboxMessageAsync(eventId);

            Assert.Equal(1, published);
            Assert.NotNull(result);
            Assert.Equal(outbox.Payload, Encoding.UTF8.GetString(result.Body.ToArray()));
            Assert.Equal("application/json", result.BasicProperties.ContentType);
            Assert.Equal(eventId.ToString(), result.BasicProperties.MessageId);
            Assert.Equal(correlationId, result.BasicProperties.CorrelationId);
            Assert.Equal("EntryCreated", result.BasicProperties.Type);
            Assert.Equal(OutboxMessageStatus.Published, outbox.Status);
            Assert.NotNull(outbox.PublishedAt);
            Assert.Null(outbox.LastError);
            Assert.Equal(0, outbox.Attempts);
        }
        finally
        {
            DeleteQueue(queueName);
            DeleteExchange(exchangeName);
        }
    }

    [Fact]
    public async Task PublishPendingAsync_sem_binding_deve_manter_pending_incrementar_attempts_e_preservar_payload()
    {
        var exchangeName = $"ledger.events.unroutable.{Guid.NewGuid():N}";
        var routingKey = $"ledger.entry.created.v1.unroutable.{Guid.NewGuid():N}";
        var eventId = Guid.NewGuid();
        var payload = CreateEntryCreatedPayload(eventId, "corr-unroutable-test");

        await InsertPendingOutboxMessageAsync(eventId, payload);
        var persistedPayload = (await GetOutboxMessageAsync(eventId)).Payload;

        try
        {
            var processor = CreateProcessor(new RabbitMqOptions
            {
                ExchangeName = exchangeName,
                RoutingKey = routingKey
            });

            var published = await processor.PublishPendingAsync(CancellationToken.None);
            var outbox = await GetOutboxMessageAsync(eventId);

            Assert.Equal(0, published);
            Assert.Equal(OutboxMessageStatus.Pending, outbox.Status);
            Assert.Equal(1, outbox.Attempts);
            Assert.NotNull(outbox.LastError);
            Assert.Contains("não foi roteada", outbox.LastError);
            Assert.Null(outbox.PublishedAt);
            Assert.Equal(persistedPayload, outbox.Payload);
        }
        finally
        {
            DeleteExchange(exchangeName);
        }
    }

    [Fact]
    public async Task PublishPendingAsync_com_falha_no_RabbitMQ_deve_manter_pending_incrementar_attempts_e_preservar_payload()
    {
        var eventId = Guid.NewGuid();
        var payload = CreateEntryCreatedPayload(eventId, "corr-failure-test");

        await InsertPendingOutboxMessageAsync(eventId, payload);
        var persistedPayload = (await GetOutboxMessageAsync(eventId)).Payload;

        var processor = CreateProcessor(new RabbitMqOptions
        {
            HostName = "rabbitmq",
            Port = 1,
            UserName = "ledger",
            Password = "ledger",
            ExchangeName = DefaultExchangeName,
            RoutingKey = DefaultRoutingKey
        });

        var published = await processor.PublishPendingAsync(CancellationToken.None);
        var outbox = await GetOutboxMessageAsync(eventId);

        Assert.Equal(0, published);
        Assert.Equal(OutboxMessageStatus.Pending, outbox.Status);
        Assert.Equal(1, outbox.Attempts);
        Assert.NotNull(outbox.LastError);
        Assert.Null(outbox.PublishedAt);
        Assert.Equal(persistedPayload, outbox.Payload);
    }

    private OutboxPublishingProcessor CreateProcessor(RabbitMqOptions? rabbitMqOptions = null)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContextFactory<LedgerDbContext>(options => options.UseNpgsql(factory.ConnectionString));
        services.Configure<RabbitMqOptions>(options =>
        {
            var source = rabbitMqOptions ?? new RabbitMqOptions();

            options.HostName = source.HostName;
            options.Port = source.Port;
            options.UserName = source.UserName;
            options.Password = source.Password;
            options.ExchangeName = source.ExchangeName;
            options.ExchangeType = source.ExchangeType;
            options.RoutingKey = source.RoutingKey;
            options.PublishConfirmTimeout = source.PublishConfirmTimeout;
        });
        services.Configure<OutboxPublisherOptions>(options =>
        {
            options.BatchSize = 20;
            options.PollingInterval = TimeSpan.FromMilliseconds(100);
        });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOutboxPublisher, RabbitMqOutboxPublisher>();
        services.AddScoped<OutboxPublishingProcessor>();

        return services.BuildServiceProvider().GetRequiredService<OutboxPublishingProcessor>();
    }

    private async Task InsertPendingOutboxMessageAsync(Guid eventId, string payload)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            OutboxId = Guid.NewGuid(),
            EventId = eventId,
            EventType = "EntryCreated",
            EventVersion = 1,
            Payload = payload,
            Status = OutboxMessageStatus.Pending,
            OccurredAt = DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-07-11T13:45:05Z"),
            Attempts = 0
        });

        await dbContext.SaveChangesAsync();
    }

    private async Task<OutboxMessage> GetOutboxMessageAsync(Guid eventId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

        return await dbContext.OutboxMessages.AsNoTracking().SingleAsync(message => message.EventId == eventId);
    }

    private static string CreateEntryCreatedPayload(Guid eventId, string correlationId)
    {
        var payload = new
        {
            eventId,
            eventType = "EntryCreated",
            eventVersion = 1,
            occurredAt = DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            createdAt = DateTimeOffset.Parse("2026-07-11T13:45:05Z"),
            correlationId,
            entryId = Guid.NewGuid(),
            merchantId = "merchant-001",
            businessDate = "2026-07-11",
            type = "CREDIT",
            amount = "150.75",
            currency = "BRL",
            description = "Venda cartão"
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static void DeclareTestQueue(string exchangeName, string routingKey, string queueName)
    {
        using var connection = CreateRabbitConnection();
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare(exchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
        channel.QueueDeclare(queueName, durable: false, exclusive: false, autoDelete: true);
        channel.QueueBind(queueName, exchangeName, routingKey);
    }

    private static async Task<BasicGetResult> GetMessageAsync(string queueName)
    {
        using var connection = CreateRabbitConnection();
        using var channel = connection.CreateModel();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var result = channel.BasicGet(queueName, autoAck: true);

            if (result is not null)
            {
                return result;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException("Mensagem publicada não foi encontrada na fila de teste.");
    }

    private static void DeleteQueue(string queueName)
    {
        using var connection = CreateRabbitConnection();
        using var channel = connection.CreateModel();

        channel.QueueDelete(queueName, ifUnused: false, ifEmpty: false);
    }

    private static void DeleteExchange(string exchangeName)
    {
        using var connection = CreateRabbitConnection();
        using var channel = connection.CreateModel();

        channel.ExchangeDelete(exchangeName, ifUnused: false);
    }

    private static IConnection CreateRabbitConnection()
    {
        var factory = new ConnectionFactory
        {
            HostName = "rabbitmq",
            Port = 5672,
            UserName = "ledger",
            Password = "ledger",
            DispatchConsumersAsync = true
        };

        return factory.CreateConnection();
    }
}

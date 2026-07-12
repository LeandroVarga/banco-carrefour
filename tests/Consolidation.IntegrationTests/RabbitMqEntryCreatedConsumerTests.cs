using BancoCarrefour.Consolidation.Application;
using BancoCarrefour.Consolidation.Persistence;
using BancoCarrefour.Consolidation.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Xunit;

namespace BancoCarrefour.Consolidation.IntegrationTests;

public sealed class RabbitMqEntryCreatedConsumerTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string ConnectionString = Environment.GetEnvironmentVariable("CONSOLIDATION_TEST_CONNECTION_STRING")
        ?? "Host=consolidation-postgres;Port=5432;Database=consolidation;Username=consolidation;Password=consolidation";

    private readonly string exchangeName = $"ledger.events.consumer.{Guid.NewGuid():N}";
    private readonly string queueName = $"consolidation-entry-created-test-{Guid.NewGuid():N}";
    private readonly string deadLetterExchangeName = $"consolidation.dlx.consumer.{Guid.NewGuid():N}";
    private readonly string deadLetterQueueName = $"consolidation-entry-created-dlq-test-{Guid.NewGuid():N}";
    private readonly string deadLetterRoutingKey = $"consolidation.entry-created.dead.{Guid.NewGuid():N}";
    private readonly string retryExchangeName = $"consolidation.retry.consumer.{Guid.NewGuid():N}";
    private readonly string retryQueueName = $"consolidation-entry-created-retry-test-{Guid.NewGuid():N}";
    private readonly string retryRoutingKey = $"consolidation.entry-created.retry.{Guid.NewGuid():N}";
    private readonly string routingKey = $"ledger.entry.created.v1.{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        DeleteQueue(queueName);
        DeleteQueue(deadLetterQueueName);
        DeleteQueue(retryQueueName);
        DeleteExchange(exchangeName);
        DeleteExchange(deadLetterExchangeName);
        DeleteExchange(retryExchangeName);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Consumer_deve_processar_entryCreated_e_criar_dailyBalance()
    {
        using var provider = CreateServiceProvider();
        using var consumer = provider.GetRequiredService<RabbitMqEntryCreatedConsumer>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var message = CreateEvent(amount: "150.75");

        consumer.Start(cancellation.Token);
        Publish(message);

        var balance = await WaitForDailyBalanceAsync("merchant-001", new DateOnly(2026, 7, 11));

        Assert.Equal(150.75m, balance.Balance);
        Assert.Equal(150.75m, balance.TotalCredits);
        Assert.Equal(0m, balance.TotalDebits);
        Assert.Equal(1, balance.EntryCount);
        Assert.Equal(1, await CountProcessedEventsAsync());
    }

    [Fact]
    public async Task Consumer_deve_ackar_duplicidade_sem_duplicar_saldo()
    {
        using var provider = CreateServiceProvider();
        using var consumer = provider.GetRequiredService<RabbitMqEntryCreatedConsumer>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var message = CreateEvent(amount: "150.75");

        consumer.Start(cancellation.Token);
        Publish(message);
        Publish(message);

        await WaitUntilAsync(async () =>
        {
            await using var context = CreateContext();
            var balance = await context.DailyBalances.AsNoTracking().SingleOrDefaultAsync();

            return balance is not null
                && balance.Balance == 150.75m
                && balance.EntryCount == 1
                && await context.ProcessedEvents.CountAsync() == 1
                && GetMessageCount(queueName) == 0;
        });

        await using var assertionContext = CreateContext();
        var persisted = await assertionContext.DailyBalances.AsNoTracking().SingleAsync();

        Assert.Equal(150.75m, persisted.Balance);
        Assert.Equal(1, persisted.EntryCount);
        Assert.Equal(1, await assertionContext.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Consumer_deve_enviar_mensagem_semanticamente_invalida_para_dlq_sem_persistir()
    {
        using var provider = CreateServiceProvider();
        using var consumer = provider.GetRequiredService<RabbitMqEntryCreatedConsumer>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        consumer.Start(cancellation.Token);
        Publish(CreateEvent(type: "INVALID"));

        await WaitUntilAsync(() => Task.FromResult(
            GetMessageCount(queueName) == 0
            && GetMessageCount(deadLetterQueueName) == 1));

        await using var assertionContext = CreateContext();
        Assert.Equal(0, await assertionContext.DailyBalances.CountAsync());
        Assert.Equal(0, await assertionContext.ProcessedEvents.CountAsync());
        Assert.Equal(1u, GetMessageCount(deadLetterQueueName));
    }

    [Fact]
    public async Task Consumer_deve_enviar_json_invalido_para_dlq_sem_persistir()
    {
        using var provider = CreateServiceProvider();
        using var consumer = provider.GetRequiredService<RabbitMqEntryCreatedConsumer>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        consumer.Start(cancellation.Token);
        Publish("""{"eventType":"EntryCreated","eventVersion":""");

        await WaitUntilAsync(() => Task.FromResult(
            GetMessageCount(queueName) == 0
            && GetMessageCount(deadLetterQueueName) == 1));

        await using var assertionContext = CreateContext();
        Assert.Equal(0, await assertionContext.DailyBalances.CountAsync());
        Assert.Equal(0, await assertionContext.ProcessedEvents.CountAsync());
        Assert.Equal(1u, GetMessageCount(deadLetterQueueName));
    }

    [Fact]
    public async Task Consumer_deve_encaminhar_erro_transitorio_para_retry_com_retry_count_incrementado()
    {
        using var provider = CreateServiceProvider(new FailingEntryCreatedProjectionProcessor());
        using var consumer = provider.GetRequiredService<RabbitMqEntryCreatedConsumer>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        consumer.Start(cancellation.Token);
        Publish(CreateEvent());

        await WaitUntilAsync(() => Task.FromResult(
            GetMessageCount(queueName) == 0
            && GetMessageCount(retryQueueName) == 1));

        Assert.Equal(1, GetMessageHeaderInt(retryQueueName, "x-retry-count"));
        Assert.Equal(0u, GetMessageCount(deadLetterQueueName));
    }

    [Fact]
    public async Task Consumer_deve_enviar_erro_transitorio_para_dlq_apos_exceder_limite_de_retries()
    {
        const int maxRetryAttempts = 3;

        using var provider = CreateServiceProvider(
            new FailingEntryCreatedProjectionProcessor(),
            maxRetryAttempts);
        using var consumer = provider.GetRequiredService<RabbitMqEntryCreatedConsumer>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        consumer.Start(cancellation.Token);
        Publish(
            CreateEvent(),
            new Dictionary<string, object>
            {
                ["x-retry-count"] = maxRetryAttempts
            });

        await WaitUntilAsync(() => Task.FromResult(
            GetMessageCount(queueName) == 0
            && GetMessageCount(deadLetterQueueName) == 1));

        Assert.Equal(maxRetryAttempts, GetMessageHeaderInt(deadLetterQueueName, "x-retry-count"));
        Assert.Equal("retry-attempts-exhausted", GetMessageHeaderString(deadLetterQueueName, "x-dead-letter-reason"));
        Assert.Equal(0u, GetMessageCount(retryQueueName));
    }

    private ServiceProvider CreateServiceProvider(
        IEntryCreatedProjectionProcessor? projectionProcessor = null,
        int maxRetryAttempts = 3)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContext<ConsolidationDbContext>(options => options.UseNpgsql(ConnectionString));
        services.AddSingleton(TimeProvider.System);
        if (projectionProcessor is null)
        {
            services.AddScoped<EntryCreatedProjectionProcessor>();
            services.AddScoped<IEntryCreatedProjectionProcessor, EntryCreatedProjectionProcessorAdapter>();
        }
        else
        {
            services.AddSingleton(projectionProcessor);
        }

        services.AddSingleton(Options.Create(new RabbitMqOptions
        {
            HostName = "rabbitmq",
            Port = 5672,
            UserName = "ledger",
            Password = "ledger",
            ExchangeName = exchangeName,
            ExchangeType = ExchangeType.Topic,
            QueueName = queueName,
            RoutingKey = routingKey,
            DeadLetterExchangeName = deadLetterExchangeName,
            DeadLetterExchangeType = ExchangeType.Direct,
            DeadLetterQueueName = deadLetterQueueName,
            DeadLetterRoutingKey = deadLetterRoutingKey,
            RetryExchangeName = retryExchangeName,
            RetryExchangeType = ExchangeType.Direct,
            RetryQueueName = retryQueueName,
            RetryRoutingKey = retryRoutingKey,
            RetryDelayMilliseconds = 60000,
            MaxRetryAttempts = maxRetryAttempts,
            PrefetchCount = 1
        }));
        services.AddSingleton<RabbitMqEntryCreatedConsumer>();

        return services.BuildServiceProvider();
    }

    private void Publish(EntryCreatedEvent message)
    {
        Publish(message, headers: null);
    }

    private void Publish(
        EntryCreatedEvent message,
        IDictionary<string, object>? headers)
    {
        Publish(JsonSerializer.Serialize(message, JsonOptions), headers);
    }

    private void Publish(string payload, IDictionary<string, object>? headers = null)
    {
        using var connection = CreateRabbitConnection();
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare(exchangeName, ExchangeType.Topic, durable: true, autoDelete: false);

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.Headers = headers;

        channel.BasicPublish(
            exchange: exchangeName,
            routingKey: routingKey,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(payload));
    }

    private async Task<BancoCarrefour.Consolidation.Persistence.Entities.DailyBalance> WaitForDailyBalanceAsync(
        string merchantId,
        DateOnly businessDate)
    {
        BancoCarrefour.Consolidation.Persistence.Entities.DailyBalance? balance = null;

        await WaitUntilAsync(async () =>
        {
            await using var context = CreateContext();
            balance = await context.DailyBalances
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.MerchantId == merchantId && x.BusinessDate == businessDate);

            return balance is not null;
        });

        return balance!;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Condição esperada não foi atingida no tempo limite.");
    }

    private uint GetMessageCount(string queue)
    {
        using var connection = CreateRabbitConnection();
        using var channel = connection.CreateModel();

        try
        {
            return channel.QueueDeclarePassive(queue).MessageCount;
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return 0;
        }
    }

    private int? GetMessageHeaderInt(string queue, string headerName)
    {
        return ReadMessageHeader(queue, headerName, ReadHeaderInt);
    }

    private string? GetMessageHeaderString(string queue, string headerName)
    {
        return ReadMessageHeader(queue, headerName, ReadHeaderString);
    }

    private static T? ReadMessageHeader<T>(
        string queue,
        string headerName,
        Func<object, T?> readValue)
    {
        using var connection = CreateRabbitConnection();
        using var channel = connection.CreateModel();

        var message = channel.BasicGet(queue, autoAck: false);
        if (message is null)
        {
            return default;
        }

        try
        {
            if (message.BasicProperties.Headers is null
                || !message.BasicProperties.Headers.TryGetValue(headerName, out var value))
            {
                return default;
            }

            return readValue(value);
        }
        finally
        {
            channel.BasicNack(message.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private static int? ReadHeaderInt(object value)
    {
        return value switch
        {
            byte retryCount => retryCount,
            short retryCount => retryCount,
            int retryCount => retryCount,
            long retryCount when retryCount <= int.MaxValue => (int)retryCount,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var retryCount) => retryCount,
            string text when int.TryParse(text, out var retryCount) => retryCount,
            _ => null
        };
    }

    private static string? ReadHeaderString(object value)
    {
        return value switch
        {
            string text => text,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => value.ToString()
        };
    }

    private static async Task<int> CountProcessedEventsAsync()
    {
        await using var context = CreateContext();

        return await context.ProcessedEvents.CountAsync();
    }

    private static async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();

        await context.Database.MigrateAsync();
        await context.ProcessedEvents.ExecuteDeleteAsync();
        await context.DailyBalances.ExecuteDeleteAsync();
    }

    private static ConsolidationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ConsolidationDbContext(options);
    }

    private static EntryCreatedEvent CreateEvent(
        string type = "CREDIT",
        string amount = "150.75")
    {
        return new EntryCreatedEvent(
            Guid.NewGuid(),
            "EntryCreated",
            1,
            DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            DateTimeOffset.Parse("2026-07-11T13:45:05Z"),
            "corr-consumer-test",
            Guid.NewGuid(),
            "merchant-001",
            "2026-07-11",
            type,
            amount,
            "BRL",
            "Venda cartão");
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

    private sealed class FailingEntryCreatedProjectionProcessor : IEntryCreatedProjectionProcessor
    {
        public Task<ProjectionResult> ProcessAsync(
            EntryCreatedEvent message,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Falha transitória controlada para teste.");
        }
    }

    private static void DeleteQueue(string queue)
    {
        using var connection = CreateRabbitConnection();
        using var channel = connection.CreateModel();

        try
        {
            channel.QueueDelete(queue, ifUnused: false, ifEmpty: false);
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
        }
    }

    private static void DeleteExchange(string exchange)
    {
        using var connection = CreateRabbitConnection();
        using var channel = connection.CreateModel();

        try
        {
            channel.ExchangeDelete(exchange, ifUnused: false);
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
        }
    }
}

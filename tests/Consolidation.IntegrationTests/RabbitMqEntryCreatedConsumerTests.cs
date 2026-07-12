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
    private readonly string routingKey = $"ledger.entry.created.v1.{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        DeleteQueue(queueName);
        DeleteExchange(exchangeName);

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
    public async Task Consumer_deve_ackar_mensagem_invalida_sem_persistir()
    {
        using var provider = CreateServiceProvider();
        using var consumer = provider.GetRequiredService<RabbitMqEntryCreatedConsumer>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        consumer.Start(cancellation.Token);
        Publish(CreateEvent(type: "INVALID"));

        await WaitUntilAsync(async () =>
        {
            await using var context = CreateContext();

            return GetMessageCount(queueName) == 0
                && await context.DailyBalances.CountAsync() == 0
                && await context.ProcessedEvents.CountAsync() == 0;
        });

        await using var assertionContext = CreateContext();
        Assert.Equal(0, await assertionContext.DailyBalances.CountAsync());
        Assert.Equal(0, await assertionContext.ProcessedEvents.CountAsync());
    }

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContext<ConsolidationDbContext>(options => options.UseNpgsql(ConnectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<EntryCreatedProjectionProcessor>();
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
            PrefetchCount = 1
        }));
        services.AddSingleton<RabbitMqEntryCreatedConsumer>();

        return services.BuildServiceProvider();
    }

    private void Publish(EntryCreatedEvent message)
    {
        Publish(JsonSerializer.Serialize(message, JsonOptions));
    }

    private void Publish(string payload)
    {
        using var connection = CreateRabbitConnection();
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare(exchangeName, ExchangeType.Topic, durable: true, autoDelete: false);

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";

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

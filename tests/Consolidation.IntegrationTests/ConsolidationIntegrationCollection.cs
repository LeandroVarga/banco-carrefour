using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using BancoCarrefour.Consolidation.Infrastructure;
using BancoCarrefour.Ledger.Infrastructure;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Testcontainers.PostgreSql;
using Xunit;

namespace BancoCarrefour.Consolidation.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class ConsolidationIntegrationCollection : ICollectionFixture<ConsolidationIntegrationTestFixture>
{
    public const string Name = "ConsolidationIntegration";
}

public sealed class ConsolidationIntegrationTestFixture : IAsyncLifetime
{
    public const string Region = "us-east-1";
    public const string AccessKey = "test";
    public const string SecretKey = "test";

    // WithTmpfsMount evita o volume anônimo que a imagem postgres declara
    // para /var/lib/postgresql/data - ver comentário completo em
    // LedgerIntegrationCollection.cs.
    private readonly PostgreSqlContainer consolidationPostgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("consolidation")
        .WithUsername("consolidation")
        .WithPassword("consolidation")
        .WithTmpfsMount("/var/lib/postgresql/data")
        .WithCleanUp(true)
        .Build();

    private readonly PostgreSqlContainer ledgerPostgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("ledger")
        .WithUsername("ledger")
        .WithPassword("ledger")
        .WithTmpfsMount("/var/lib/postgresql/data")
        .WithCleanUp(true)
        .Build();

    // Mesma versão exata fixada em docker-compose.yml (localstack/localstack:4.14.0,
    // digest sha256:3ebc37595918b8accb852f8048fef2aff047d465167edd655528065b07bc364a).
    // Testcontainers 3.10.0 não aceita o formato combinado "tag@digest" em
    // WithImage(string) (DotNet.Testcontainers.Images.MatchImage rejeita - mesma
    // restrição documentada em IdentityFixture.cs para o Keycloak) - a imagem já
    // cacheada localmente sob esta tag é a mesma resolvida pelo digest acima.
    private const string LocalStackImage = "localstack/localstack:4.14.0";

    private readonly IContainer localStack = new ContainerBuilder()
        .WithImage(LocalStackImage)
        .WithEnvironment("SERVICES", "sqs")
        .WithEnvironment("AWS_DEFAULT_REGION", Region)
        .WithEnvironment("DEFAULT_REGION", Region)
        .WithPortBinding(4566, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
            .ForPort(4566)
            .ForPath("/_localstack/health")))
        .WithCleanUp(true)
        .Build();

    public string ConsolidationConnectionString => consolidationPostgres.GetConnectionString();

    public string LedgerConnectionString => ledgerPostgres.GetConnectionString();

    public string SqsServiceUrl => $"http://{localStack.Hostname}:{localStack.GetMappedPublicPort(4566)}";

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            consolidationPostgres.StartAsync(),
            ledgerPostgres.StartAsync(),
            localStack.StartAsync());

        await MigrateConsolidationAsync();
        await MigrateLedgerAsync();
        await ResetConsolidationDatabaseAsync();
        await ResetLedgerDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        var exceptions = new List<Exception>();

        await DisposeSafelyAsync(localStack.DisposeAsync, exceptions);
        await DisposeSafelyAsync(ledgerPostgres.DisposeAsync, exceptions);
        await DisposeSafelyAsync(consolidationPostgres.DisposeAsync, exceptions);

        if (exceptions.Count > 0)
        {
            throw new AggregateException(
                "Falha ao descartar um ou mais containers da fixture de integração do Consolidation.",
                exceptions);
        }
    }

    private static async Task DisposeSafelyAsync(Func<ValueTask> disposeAsync, List<Exception> exceptions)
    {
        try
        {
            await disposeAsync();
        }
        catch (Exception exception)
        {
            exceptions.Add(exception);
        }
    }

    public AmazonSQSClient CreateSqsClient()
    {
        return new AmazonSQSClient(
            new BasicAWSCredentials(AccessKey, SecretKey),
            new AmazonSQSConfig
            {
                ServiceURL = SqsServiceUrl,
                AuthenticationRegion = Region
            });
    }

    public async Task<string> CreateQueueAsync(
        IAmazonSQS sqs,
        string name,
        int visibilityTimeoutSeconds = 5,
        int? maxReceiveCount = null,
        string? deadLetterQueueArn = null)
    {
        var attributes = new Dictionary<string, string>
        {
            [QueueAttributeName.VisibilityTimeout] = visibilityTimeoutSeconds.ToString(),
            [QueueAttributeName.ReceiveMessageWaitTimeSeconds] = "0"
        };

        if (maxReceiveCount is not null && deadLetterQueueArn is not null)
        {
            attributes[QueueAttributeName.RedrivePolicy] = JsonSerializer.Serialize(new
            {
                deadLetterTargetArn = deadLetterQueueArn,
                maxReceiveCount
            });
        }

        var response = await sqs.CreateQueueAsync(new CreateQueueRequest
        {
            QueueName = name,
            Attributes = attributes
        });

        return response.QueueUrl;
    }

    public async Task ResetConsolidationDatabaseAsync()
    {
        await using var dbContext = CreateConsolidationContext();

        await dbContext.ProcessedEvents.ExecuteDeleteAsync();
        await dbContext.DailyBalances.ExecuteDeleteAsync();
    }

    public async Task ResetLedgerDatabaseAsync()
    {
        await using var dbContext = CreateLedgerContext();

        await dbContext.OutboxMessages.ExecuteDeleteAsync();
        await dbContext.InputIdempotencyRecords.ExecuteDeleteAsync();
        await dbContext.Entries.ExecuteDeleteAsync();
    }

    private async Task MigrateConsolidationAsync()
    {
        await using var dbContext = CreateConsolidationContext();
        await dbContext.Database.MigrateAsync();
    }

    private async Task MigrateLedgerAsync()
    {
        await using var dbContext = CreateLedgerContext();
        await dbContext.Database.MigrateAsync();
    }

    private ConsolidationDbContext CreateConsolidationContext()
    {
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(ConsolidationConnectionString)
            .Options;

        return new ConsolidationDbContext(options);
    }

    private LedgerDbContext CreateLedgerContext()
    {
        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseNpgsql(LedgerConnectionString)
            .Options;

        return new LedgerDbContext(options);
    }
}

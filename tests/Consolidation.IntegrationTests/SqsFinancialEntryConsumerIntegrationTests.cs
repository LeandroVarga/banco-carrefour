using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using BancoCarrefour.Consolidation.Application;
using BancoCarrefour.Consolidation.Application.ApplyFinancialEntry;
using BancoCarrefour.Consolidation.Infrastructure;
using BancoCarrefour.Consolidation.Infrastructure.DailyBalances;
using BancoCarrefour.Consolidation.Worker.Sqs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace BancoCarrefour.Consolidation.IntegrationTests;

[Collection(ConsolidationIntegrationCollection.Name)]
public sealed class SqsFinancialEntryConsumerIntegrationTests : IAsyncLifetime
{
    private readonly ConsolidationIntegrationTestFixture fixture;
    private readonly string queueName = $"financial-entry-registered-test-{Guid.NewGuid():N}";
    private readonly AmazonSQSClient sqs;
    private string queueUrl = string.Empty;

    public SqsFinancialEntryConsumerIntegrationTests(ConsolidationIntegrationTestFixture fixture)
    {
        this.fixture = fixture;
        sqs = fixture.CreateSqsClient();
    }

    public async Task InitializeAsync()
    {
        await fixture.ResetConsolidationDatabaseAsync();
        queueUrl = await fixture.CreateQueueAsync(sqs, queueName);
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(queueUrl))
        {
            await sqs.DeleteQueueAsync(queueUrl);
        }

        sqs.Dispose();
    }

    [Fact]
    public async Task PollOnceAsync_processa_mensagem_sqs_exclui_da_fila_e_atualiza_dailyBalance()
    {
        await sqs.SendMessageAsync(queueUrl, CreateMessageBody());
        using var provider = CreateServiceProvider(queueUrl);
        var consumer = provider.GetRequiredService<SqsFinancialEntryConsumer>();

        await consumer.PollOnceAsync(CancellationToken.None);

        await using var dbContext = CreateContext();
        var balance = await dbContext.DailyBalances.AsNoTracking().SingleAsync();
        var remaining = await ReceiveFromQueueAsync(queueUrl);

        Assert.Equal("merchant-001", balance.MerchantId);
        Assert.Equal(DateOnly.Parse("2026-07-11"), balance.BusinessDate);
        Assert.Equal(150.75m, balance.TotalCredits);
        Assert.Empty(remaining.Messages);
    }

    [Fact]
    public async Task PollOnceAsync_falha_antes_do_commit_nao_exclui_e_redelivery_aplica_uma_vez()
    {
        var eventId = Guid.NewGuid();
        await sqs.SendMessageAsync(queueUrl, CreateMessageBody(eventId: eventId));
        using var provider = CreateServiceProvider(
            queueUrl,
            visibilityTimeoutSeconds: 1,
            useCaseFactory: () => new FailsOnceBeforeCommitUseCase(CreateRealUseCase()));
        var consumer = provider.GetRequiredService<SqsFinancialEntryConsumer>();

        await consumer.PollOnceAsync(CancellationToken.None);

        await using (var dbContext = CreateContext())
        {
            Assert.Equal(0, await dbContext.ProcessedEvents.CountAsync());
            Assert.Equal(0, await dbContext.DailyBalances.CountAsync());
        }

        await PollUntilProcessedAsync(consumer, eventId);

        await using (var dbContext = CreateContext())
        {
            var balance = await dbContext.DailyBalances.AsNoTracking().SingleAsync();
            Assert.Equal(150.75m, balance.TotalCredits);
            Assert.Equal(1, balance.EntryCount);
            Assert.Equal(1, await dbContext.ProcessedEvents.CountAsync(x => x.EventId == eventId));
        }

        var remaining = await ReceiveFromQueueAsync(queueUrl);
        Assert.Empty(remaining.Messages);
    }

    [Fact]
    public async Task PollOnceAsync_commit_sem_delete_redelivery_duplicado_nao_reaplica_saldo_e_exclui()
    {
        var eventId = Guid.NewGuid();
        var body = CreateMessageBody(eventId: eventId);
        await sqs.SendMessageAsync(queueUrl, body);
        var firstReceive = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 1,
            VisibilityTimeout = 1
        });
        var message = Assert.Single(firstReceive.Messages);
        var command = FinancialEntryMessageParser.Parse(message.Body);
        await CreateRealUseCase().ApplyAsync(command, CancellationToken.None);
        using var provider = CreateServiceProvider(queueUrl, visibilityTimeoutSeconds: 1);
        var consumer = provider.GetRequiredService<SqsFinancialEntryConsumer>();

        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        await consumer.PollOnceAsync(CancellationToken.None);

        await using var dbContext = CreateContext();
        var balance = await dbContext.DailyBalances.AsNoTracking().SingleAsync();
        var remaining = await ReceiveFromQueueAsync(queueUrl);

        Assert.Equal(eventId, command.EventId);
        Assert.Equal(150.75m, balance.TotalCredits);
        Assert.Equal(1, balance.EntryCount);
        Assert.Equal(1, await dbContext.ProcessedEvents.CountAsync(x => x.EventId == eventId));
        Assert.Empty(remaining.Messages);
    }

    [Fact]
    public async Task Mensagem_invalida_nao_excluida_vai_para_dlq_por_redrive_no_localstack()
    {
        var dlqUrl = await fixture.CreateQueueAsync(
            sqs,
            $"financial-entry-registered-dlq-test-{Guid.NewGuid():N}",
            visibilityTimeoutSeconds: 1);
        try
        {
            var dlqArn = await GetQueueArnAsync(dlqUrl);
            var mainQueueUrl = await fixture.CreateQueueAsync(
                sqs,
                $"financial-entry-registered-redrive-test-{Guid.NewGuid():N}",
                visibilityTimeoutSeconds: 1,
                maxReceiveCount: 2,
                deadLetterQueueArn: dlqArn);

            try
            {
                const string invalidPayload = """{"eventType":"FinancialEntryRegistered","eventVersion":1}""";
                await sqs.SendMessageAsync(mainQueueUrl, invalidPayload);
                using var provider = CreateServiceProvider(mainQueueUrl, visibilityTimeoutSeconds: 1);
                var consumer = provider.GetRequiredService<SqsFinancialEntryConsumer>();
                var observedReceiveCounts = new List<int>();

                for (var attempt = 0; attempt < 5; attempt++)
                {
                    await consumer.PollOnceAsync(CancellationToken.None);
                    await Task.Delay(TimeSpan.FromMilliseconds(1200));

                    var dlqMessage = await ReceiveFromQueueAsync(dlqUrl);
                    if (dlqMessage.Messages.Count > 0)
                    {
                        Assert.Equal(invalidPayload, dlqMessage.Messages[0].Body);
                        if (observedReceiveCounts.Count > 0)
                        {
                            Assert.Contains(observedReceiveCounts, count => count >= 2);
                        }

                        return;
                    }

                    var probe = await ReceiveFromQueueAsync(mainQueueUrl, visibilityTimeoutSeconds: 1);
                    if (probe.Messages.Count > 0)
                    {
                        observedReceiveCounts.Add(GetReceiveCount(probe.Messages[0]));
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(1200));
                }

                throw new Xunit.Sdk.XunitException("Mensagem inválida não foi movida para DLQ no tempo esperado.");
            }
            finally
            {
                await DeleteQueueQuietlyAsync(mainQueueUrl);
            }
        }
        finally
        {
            await DeleteQueueQuietlyAsync(dlqUrl);
        }
    }

    private ServiceProvider CreateServiceProvider(
        string targetQueueUrl,
        int visibilityTimeoutSeconds = 5,
        Func<IApplyFinancialEntryUseCase>? useCaseFactory = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddDbContext<ConsolidationDbContext>(options => options.UseNpgsql(fixture.ConsolidationConnectionString));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IAmazonSQS>(sqs);
        services.AddSingleton<IOptions<SqsOptions>>(Options.Create(new SqsOptions
        {
            QueueUrl = targetQueueUrl,
            ServiceUrl = fixture.SqsServiceUrl,
            Region = ConsolidationIntegrationTestFixture.Region,
            AccessKey = ConsolidationIntegrationTestFixture.AccessKey,
            SecretKey = ConsolidationIntegrationTestFixture.SecretKey,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 0,
            VisibilityTimeoutSeconds = visibilityTimeoutSeconds
        }));

        if (useCaseFactory is null)
        {
            services.AddScoped<IDailyBalanceProjectionStore, EfDailyBalanceProjectionStore>();
            services.AddScoped<IApplyFinancialEntryUseCase, ApplyFinancialEntryUseCase>();
        }
        else
        {
            services.AddSingleton(useCaseFactory());
        }

        services.AddSingleton<SqsFinancialEntryConsumer>();

        return services.BuildServiceProvider();
    }

    private IApplyFinancialEntryUseCase CreateRealUseCase()
    {
        return new ApplyFinancialEntryUseCase(
            new EfDailyBalanceProjectionStore(CreateContext()),
            TimeProvider.System);
    }

    private ConsolidationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(fixture.ConsolidationConnectionString)
            .Options;

        return new ConsolidationDbContext(options);
    }

    private async Task<string> GetQueueArnAsync(string targetQueueUrl)
    {
        var attributes = await sqs.GetQueueAttributesAsync(targetQueueUrl, [QueueAttributeName.QueueArn]);

        return attributes.QueueARN;
    }

    private Task<ReceiveMessageResponse> ReceiveFromQueueAsync(string targetQueueUrl, int visibilityTimeoutSeconds = 1)
    {
        return sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = targetQueueUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 0,
            VisibilityTimeout = visibilityTimeoutSeconds,
            MessageSystemAttributeNames = ["ApproximateReceiveCount"]
        });
    }

    private async Task PollUntilProcessedAsync(SqsFinancialEntryConsumer consumer, Guid eventId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1200));
            await consumer.PollOnceAsync(CancellationToken.None);

            await using var dbContext = CreateContext();
            if (await dbContext.ProcessedEvents.AnyAsync(x => x.EventId == eventId))
            {
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("Mensagem SQS não foi reentregue e processada no tempo esperado.");
    }

    private async Task DeleteQueueQuietlyAsync(string targetQueueUrl)
    {
        try
        {
            await sqs.DeleteQueueAsync(targetQueueUrl);
        }
        catch (AmazonSQSException)
        {
        }
    }

    private static int GetReceiveCount(Message message)
    {
        return message.Attributes.TryGetValue("ApproximateReceiveCount", out var value)
            && int.TryParse(value, out var count)
                ? count
                : 0;
    }

    private static string CreateMessageBody(Guid? eventId = null)
    {
        return $$"""
            {
              "eventId": "{{eventId ?? Guid.NewGuid()}}",
              "entryId": "{{Guid.NewGuid()}}",
              "eventType": "FinancialEntryRegistered",
              "eventVersion": 1,
              "occurredAt": "2026-07-11T13:45:00Z",
              "registeredAt": "2026-07-11T13:45:05Z",
              "correlationId": "corr-sqs-it",
              "merchantId": "merchant-001",
              "businessDate": "2026-07-11",
              "type": "CREDIT",
              "amount": "150.75",
              "currency": "BRL",
              "description": "Venda cartão"
            }
            """;
    }

    private sealed class FailsOnceBeforeCommitUseCase(IApplyFinancialEntryUseCase inner) : IApplyFinancialEntryUseCase
    {
        private int calls;

        public Task<ProjectionResult> ApplyAsync(
            ApplyFinancialEntryCommand command,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("falha transitória antes do commit");
            }

            return inner.ApplyAsync(command, cancellationToken);
        }
    }
}

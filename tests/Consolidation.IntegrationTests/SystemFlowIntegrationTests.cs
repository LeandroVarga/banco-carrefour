extern alias LedgerApi;

using Amazon.SQS;
using Amazon.SQS.Model;
using BancoCarrefour.Consolidation.Application;
using BancoCarrefour.Consolidation.Application.ApplyFinancialEntry;
using BancoCarrefour.Consolidation.Infrastructure;
using BancoCarrefour.Consolidation.Infrastructure.DailyBalances;
using BancoCarrefour.Consolidation.Worker.Sqs;
using BancoCarrefour.Ledger.Application.PublishOutbox;
using BancoCarrefour.Ledger.Infrastructure;
using BancoCarrefour.Ledger.Infrastructure.Entities;
using BancoCarrefour.Ledger.Infrastructure.Outbox;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Claims;
using Xunit;
using LedgerOutboxSqsOptions = BancoCarrefour.Ledger.Infrastructure.Outbox.SqsOptions;
using WorkerSqsOptions = BancoCarrefour.Consolidation.Worker.Sqs.SqsOptions;

namespace BancoCarrefour.Consolidation.IntegrationTests;

[Collection(ConsolidationIntegrationCollection.Name)]
public sealed class SystemFlowIntegrationTests
{
    private const string Issuer = "https://test-issuer.local/realms/banco-carrefour";
    private const string LedgerAudience = "ledger-api";
    private const string ConsolidationAudience = "consolidation-api";

    // Chave RSA exclusiva deste teste (RS256 real, nunca HS256) - o fluxo
    // sistêmico emite um único token com as duas audiences/scopes, como o
    // Keycloak real faria quando ambos os scopes são solicitados juntos.
    private static readonly RsaSecurityKey TestSigningKey = new(RSA.Create(2048));
    private readonly ConsolidationIntegrationTestFixture fixture;

    public SystemFlowIntegrationTests(ConsolidationIntegrationTestFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task Fluxo_sistemico_registra_publica_consume_e_consulta_dailyBalance()
    {
        await ResetDatabasesAsync();
        using var ledgerFactory = new LedgerSystemApiFactory(fixture.LedgerConnectionString);
        using var consolidationFactory = new ConsolidationSystemApiFactory(fixture.ConsolidationConnectionString);
        using var sqs = fixture.CreateSqsClient();
        var queueName = $"financial-entry-registered-e2e-{Guid.NewGuid():N}";
        var queueUrl = await fixture.CreateQueueAsync(sqs, queueName);
        var correlationId = $"corr-e2e-{Guid.NewGuid():N}";
        var idempotencyKey = $"idem-e2e-{Guid.NewGuid():N}";

        try
        {
            using var ledgerClient = CreateAuthenticatedClient(ledgerFactory, "merchant-001");
            var postResponse = await PostEntryAsync(ledgerClient, idempotencyKey, correlationId);
            var postBody = await ReadJsonAsync(postResponse);
            var entryId = postBody.RootElement.GetProperty("entryId").GetGuid();
            var outboxBeforePublish = await ReadSingleOutboxAsync(fixture.LedgerConnectionString, correlationId);
            var payload = JsonNode.Parse(outboxBeforePublish.Payload)!;

            Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
            Assert.NotEqual(Guid.Empty, outboxBeforePublish.EventId);
            Assert.Equal(entryId.ToString(), payload["entryId"]!.GetValue<string>());
            Assert.Equal(OutboxMessageStatus.Pending, outboxBeforePublish.Status);
            Assert.Equal("FinancialEntryRegistered", payload["eventType"]!.GetValue<string>());
            Assert.Equal(correlationId, payload["correlationId"]!.GetValue<string>());

            var publishResult = await PublishOutboxAsync(fixture.LedgerConnectionString, sqs, queueUrl);
            var outboxAfterPublish = await ReadSingleOutboxAsync(fixture.LedgerConnectionString, correlationId);

            Assert.Equal(1, publishResult.Claimed);
            Assert.Equal(1, publishResult.Published);
            Assert.Equal(0, publishResult.Failed);
            Assert.Equal(OutboxMessageStatus.Published, outboxAfterPublish.Status);
            Assert.Equal(outboxBeforePublish.EventId, outboxAfterPublish.EventId);
            Assert.NotNull(outboxAfterPublish.PublishedAt);

            var publishedMessage = await ReceivePublishedMessageAsync(sqs, queueUrl);
            Assert.Contains(outboxBeforePublish.EventId.ToString(), publishedMessage.Body, StringComparison.Ordinal);
            Assert.Contains(correlationId, publishedMessage.Body, StringComparison.Ordinal);
            await sqs.ChangeMessageVisibilityAsync(queueUrl, publishedMessage.ReceiptHandle, 0);

            await ConsumeUntilProcessedAsync(consolidationFactory, sqs, queueUrl, outboxBeforePublish.EventId);

            await using (var consolidationScope = consolidationFactory.Services.CreateAsyncScope())
            {
                var dbContext = consolidationScope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();
                var processed = await dbContext.ProcessedEvents.AsNoTracking().SingleAsync();
                var balance = await dbContext.DailyBalances.AsNoTracking().SingleAsync();

                Assert.Equal(outboxBeforePublish.EventId, processed.EventId);
                Assert.Equal("merchant-001", processed.MerchantId);
                Assert.Equal("merchant-001", balance.MerchantId);
                Assert.Equal(new DateOnly(2026, 7, 11), balance.BusinessDate);
                Assert.Equal(150.75m, balance.TotalCredits);
                Assert.Equal(0m, balance.TotalDebits);
                Assert.Equal(150.75m, balance.Balance);
                Assert.Equal(1, balance.EntryCount);
            }

            using var consolidationClient = CreateAuthenticatedClient(consolidationFactory, "merchant-001");
            var getResponse = await GetDailyBalanceAsync(consolidationClient);
            var getBody = await ReadJsonAsync(getResponse);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            Assert.Equal("merchant-001", getBody.RootElement.GetProperty("merchantId").GetString());
            Assert.Equal("2026-07-11", getBody.RootElement.GetProperty("businessDate").GetString());
            Assert.Equal("150.75", getBody.RootElement.GetProperty("totalCredits").GetString());
            Assert.Equal("0.00", getBody.RootElement.GetProperty("totalDebits").GetString());
            Assert.Equal("150.75", getBody.RootElement.GetProperty("balance").GetString());
            Assert.Equal(1, getBody.RootElement.GetProperty("entriesCount").GetInt64());

            var remaining = await sqs.ReceiveMessageAsync(queueUrl);
            Assert.Empty(remaining.Messages);
        }
        finally
        {
            await sqs.DeleteQueueAsync(queueUrl);
        }
    }

    [Fact]
    public async Task Post_entries_independe_do_consolidation_e_processa_quando_consolidation_retorna()
    {
        await ResetDatabasesAsync();
        using var ledgerFactory = new LedgerSystemApiFactory(fixture.LedgerConnectionString);
        using var sqs = fixture.CreateSqsClient();
        var queueName = $"financial-entry-registered-recovery-{Guid.NewGuid():N}";
        var queueUrl = await fixture.CreateQueueAsync(sqs, queueName);
        var correlationId = $"corr-recovery-{Guid.NewGuid():N}";
        var idempotencyKey = $"idem-recovery-{Guid.NewGuid():N}";

        try
        {
            using var ledgerClient = CreateAuthenticatedClient(ledgerFactory, "merchant-001");
            var postResponse = await PostEntryAsync(ledgerClient, idempotencyKey, correlationId);
            var postBody = await ReadJsonAsync(postResponse);
            var entryId = postBody.RootElement.GetProperty("entryId").GetGuid();
            var outboxBeforeRecovery = await ReadSingleOutboxAsync(fixture.LedgerConnectionString, correlationId);

            Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
            Assert.NotEqual(Guid.Empty, entryId);
            Assert.Equal(OutboxMessageStatus.Pending, outboxBeforeRecovery.Status);
            Assert.Null(outboxBeforeRecovery.PublishedAt);

            using var consolidationFactory = new ConsolidationSystemApiFactory(fixture.ConsolidationConnectionString);

            var publishResult = await PublishOutboxAsync(fixture.LedgerConnectionString, sqs, queueUrl);
            var outboxAfterPublish = await ReadSingleOutboxAsync(fixture.LedgerConnectionString, correlationId);

            Assert.Equal(1, publishResult.Published);
            Assert.Equal(OutboxMessageStatus.Published, outboxAfterPublish.Status);

            await ConsumeUntilProcessedAsync(consolidationFactory, sqs, queueUrl, outboxBeforeRecovery.EventId);

            using var consolidationClient = CreateAuthenticatedClient(consolidationFactory, "merchant-001");
            var getResponse = await GetDailyBalanceAsync(consolidationClient);
            var getBody = await ReadJsonAsync(getResponse);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            Assert.Equal("150.75", getBody.RootElement.GetProperty("balance").GetString());
            Assert.Equal(1, getBody.RootElement.GetProperty("entriesCount").GetInt64());
        }
        finally
        {
            await sqs.DeleteQueueAsync(queueUrl);
        }
    }

    /// <summary>
    /// Prova de isolamento de disponibilidade (fechamento ): o lado
    /// de PROCESSAMENTO do Consolidado (worker/consumer) fica indisponível -
    /// nenhuma instância de <see cref="ConsolidationSystemApiFactory"/> nem de
    /// <see cref="SqsFinancialEntryConsumer"/> existe até o marco 6. Isso
    /// difere deliberadamente de só derrubar o Consolidation.Api (caminho de
    /// LEITURA) - a exigência original é sobre o caminho de escrita/consolidação
    /// continuar funcionando mesmo sem NENHUM processamento do Consolidado.
    /// </summary>
    [Fact]
    public async Task Consolidation_indisponivel_nao_bloqueia_Ledger_e_converge_apos_recuperacao_sem_duplicar_saldo()
    {
        await ResetDatabasesAsync();
        using var ledgerFactory = new LedgerSystemApiFactory(fixture.LedgerConnectionString);
        using var sqs = fixture.CreateSqsClient();
        var queueName = $"financial-entry-registered-isolation-{Guid.NewGuid():N}";
        var queueUrl = await fixture.CreateQueueAsync(sqs, queueName);
        var correlationId = $"corr-isolation-{Guid.NewGuid():N}";
        var idempotencyKey = $"idem-isolation-{Guid.NewGuid():N}";

        try
        {
            // MARCO 1: Consolidation (Worker e Api) indisponível por construção -
            // nenhuma factory/consumer de Consolidation existe neste ponto.
            using var ledgerClient = CreateAuthenticatedClient(ledgerFactory, "merchant-001");
            var postResponse = await PostEntryAsync(ledgerClient, idempotencyKey, correlationId);
            Assert.True(
                postResponse.StatusCode == HttpStatusCode.Created,
                $"MARCO 2 FALHOU: Ledger deveria aceitar o lançamento (201) mesmo com Consolidation indisponível - obtido {postResponse.StatusCode}.");

            // MARCO 3: lançamento e evento da Outbox persistidos, ainda pendentes.
            var outboxPending = await ReadSingleOutboxAsync(fixture.LedgerConnectionString, correlationId);
            Assert.Equal(OutboxMessageStatus.Pending, outboxPending.Status);

            // MARCO 4: Ledger.OutboxPublisher publica no SQS de forma totalmente
            // independente de Consolidation (nenhuma factory de Consolidation
            // existe ainda neste ponto da execução).
            var publishResult = await PublishOutboxAsync(fixture.LedgerConnectionString, sqs, queueUrl);
            Assert.Equal(1, publishResult.Published);
            var outboxPublished = await ReadSingleOutboxAsync(fixture.LedgerConnectionString, correlationId);
            Assert.Equal(OutboxMessageStatus.Published, outboxPublished.Status);

            // MARCO 5: com Consolidation ainda indisponível (nenhum consumer
            // rodando), o evento permanece pendente/não processado na fila.
            var peek = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                MaxNumberOfMessages = 1,
                WaitTimeSeconds = 1,
                VisibilityTimeout = 30
            });
            Assert.True(Assert.Single(peek.Messages).Body.Contains(outboxPublished.EventId.ToString(), StringComparison.Ordinal),
                "MARCO 5 FALHOU: evento publicado deveria continuar na fila, não processado, enquanto Consolidation está indisponível.");
            // Libera a visibilidade de volta (nenhum consumer real a reclamou).
            await sqs.ChangeMessageVisibilityAsync(queueUrl, peek.Messages[0].ReceiptHandle, 0);

            // MARCO 6: restaura o processamento de Consolidation (Worker + Api).
            using var consolidationFactory = new ConsolidationSystemApiFactory(fixture.ConsolidationConnectionString);
            await ConsumeUntilProcessedAsync(consolidationFactory, sqs, queueUrl, outboxPublished.EventId);

            // MARCO 7: convergência da projeção, sem nenhum reparo manual de banco.
            await using (var scope = consolidationFactory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();
                var balance = await dbContext.DailyBalances.AsNoTracking().SingleAsync();
                Assert.Equal(150.75m, balance.Balance);
                Assert.Equal(1, balance.EntryCount);
            }

            using var consolidationClient = CreateAuthenticatedClient(consolidationFactory, "merchant-001");
            var getResponse = await GetDailyBalanceAsync(consolidationClient);
            Assert.True(
                getResponse.StatusCode == HttpStatusCode.OK,
                $"MARCO 7 FALHOU: GET /daily-balances deveria retornar 200 após a recuperação - obtido {getResponse.StatusCode}.");

            // MARCO 8: reentrega duplicada do MESMO evento (simulando entrega
            // at-least-once real) não duplica o saldo - reaproveita o
            // SqsFinancialEntryConsumer real (nunca reimplementado).
            await sqs.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = outboxPublished.Payload
            });
            await PollOnceAsync(consolidationFactory, sqs, queueUrl);

            var remainingAfterDuplicate = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                MaxNumberOfMessages = 1,
                WaitTimeSeconds = 1
            });
            Assert.Empty(remainingAfterDuplicate.Messages);

            await using (var scope = consolidationFactory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();
                var balanceAfterDuplicate = await dbContext.DailyBalances.AsNoTracking().SingleAsync();
                Assert.True(
                    balanceAfterDuplicate.Balance == 150.75m && balanceAfterDuplicate.EntryCount == 1,
                    $"MARCO 8 FALHOU: reentrega duplicada não deveria alterar o saldo - obtido balance={balanceAfterDuplicate.Balance}, entryCount={balanceAfterDuplicate.EntryCount}.");
                var processedCount = await dbContext.ProcessedEvents.CountAsync(x => x.EventId == outboxPublished.EventId);
                Assert.Equal(1, processedCount);
            }
        }
        finally
        {
            await sqs.DeleteQueueAsync(queueUrl);
        }
    }

    /// <summary>
    /// Uma única passagem de consumo, sem esperar por convergência - usada
    /// somente para a prova de reentrega duplicada (MARCO 8), onde o evento já
    /// foi processado antes e <see cref="ConsumeUntilProcessedAsync"/> retornaria
    /// imediatamente sem de fato consumir a mensagem redelivered.
    /// </summary>
    private async Task PollOnceAsync(
        ConsolidationSystemApiFactory factory,
        AmazonSQSClient sqs,
        string queueUrl)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IApplyFinancialEntryUseCase>(new CapturingApplyFinancialEntryUseCase(factory.ConnectionString));
        services.AddSingleton<IAmazonSQS>(sqs);
        services.AddSingleton<IOptions<WorkerSqsOptions>>(Options.Create(new WorkerSqsOptions
        {
            QueueUrl = queueUrl,
            ServiceUrl = fixture.SqsServiceUrl,
            Region = ConsolidationIntegrationTestFixture.Region,
            AccessKey = ConsolidationIntegrationTestFixture.AccessKey,
            SecretKey = ConsolidationIntegrationTestFixture.SecretKey,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 2,
            VisibilityTimeoutSeconds = 5
        }));
        services.AddSingleton<SqsFinancialEntryConsumer>();

        using var provider = services.BuildServiceProvider();
        var consumer = provider.GetRequiredService<SqsFinancialEntryConsumer>();

        await consumer.PollOnceAsync(CancellationToken.None);
    }

    private async Task ResetDatabasesAsync()
    {
        await fixture.ResetLedgerDatabaseAsync();
        await fixture.ResetConsolidationDatabaseAsync();
    }

    private static HttpClient CreateAuthenticatedClient<TProgram>(
        WebApplicationFactory<TProgram> factory,
        string merchantId)
        where TProgram : class
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(merchantId));

        return client;
    }

    private static async Task<HttpResponseMessage> PostEntryAsync(
        HttpClient client,
        string idempotencyKey,
        string correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/entries")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    type = "CREDIT",
                    amount = "150.75",
                    currency = "BRL",
                    occurredAt = DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
                    description = "Venda cartão"
                }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", correlationId);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetDailyBalanceAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/daily-balances/2026-07-11");
        request.Headers.Add("X-Correlation-Id", "corr-e2e");

        return await client.SendAsync(request);
    }

    private static async Task<OutboxMessage> ReadSingleOutboxAsync(
        string ledgerConnectionString,
        string correlationId)
    {
        await using var dbContext = CreateLedgerContext(ledgerConnectionString);
        var messages = await dbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        return messages.Single(message => message.Payload.Contains(correlationId, StringComparison.Ordinal));
    }

    private async Task<OutboxPublishResult> PublishOutboxAsync(
        string ledgerConnectionString,
        AmazonSQSClient sqs,
        string queueUrl)
    {
        await using var dbContext = CreateLedgerContext(ledgerConnectionString);
        var useCase = new PublishPendingEventsUseCase(
            new PostgresOutboxStore(dbContext, TimeProvider.System),
            new SqsIntegrationEventPublisher(
                sqs,
                Options.Create(new LedgerOutboxSqsOptions
                {
                    QueueUrl = queueUrl,
                    ServiceUrl = fixture.SqsServiceUrl,
                    Region = ConsolidationIntegrationTestFixture.Region,
                    AccessKey = ConsolidationIntegrationTestFixture.AccessKey,
                    SecretKey = ConsolidationIntegrationTestFixture.SecretKey
                }),
                NullLogger<SqsIntegrationEventPublisher>.Instance),
            new PublisherInstance("system-flow-test"),
            TimeProvider.System,
            new OutboxPublishingOptions
            {
                BatchSize = 10,
                ClaimTimeout = TimeSpan.FromMinutes(2),
                BaseRetryDelay = TimeSpan.FromMilliseconds(100),
                MaxRetryDelay = TimeSpan.FromSeconds(1)
            });

        return await useCase.PublishAsync(CancellationToken.None);
    }

    private static LedgerDbContext CreateLedgerContext(string ledgerConnectionString)
    {
        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseNpgsql(ledgerConnectionString)
            .Options;

        return new LedgerDbContext(options);
    }

    private static async Task<Message> ReceivePublishedMessageAsync(
        AmazonSQSClient sqs,
        string queueUrl)
    {
        var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 2,
            VisibilityTimeout = 5,
            MessageAttributeNames = ["All"]
        });

        return Assert.Single(response.Messages);
    }

    private async Task ConsumeUntilProcessedAsync(
        ConsolidationSystemApiFactory factory,
        AmazonSQSClient sqs,
        string queueUrl,
        Guid eventId)
    {
        var services = new ServiceCollection();
        var capturingUseCase = new CapturingApplyFinancialEntryUseCase(factory.ConnectionString);
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IApplyFinancialEntryUseCase>(capturingUseCase);
        services.AddSingleton<IAmazonSQS>(sqs);
        services.AddSingleton<IOptions<WorkerSqsOptions>>(Options.Create(new WorkerSqsOptions
        {
            QueueUrl = queueUrl,
            ServiceUrl = fixture.SqsServiceUrl,
            Region = ConsolidationIntegrationTestFixture.Region,
            AccessKey = ConsolidationIntegrationTestFixture.AccessKey,
            SecretKey = ConsolidationIntegrationTestFixture.SecretKey,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 1,
            VisibilityTimeoutSeconds = 5
        }));
        services.AddSingleton<SqsFinancialEntryConsumer>();

        using var provider = services.BuildServiceProvider();
        var consumer = provider.GetRequiredService<SqsFinancialEntryConsumer>();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await consumer.PollOnceAsync(CancellationToken.None);

            await using var scope = factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();
            if (await dbContext.ProcessedEvents.AnyAsync(x => x.EventId == eventId))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new Xunit.Sdk.XunitException(
            $"Evento publicado no SQS não foi processado pelo Consolidation no tempo esperado. Último erro: {capturingUseCase.LastError}");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();

        return await JsonDocument.ParseAsync(stream);
    }

    private static string CreateToken(string merchantId)
    {
        var credentials = new SigningCredentials(TestSigningKey, SecurityAlgorithms.RsaSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "system-flow-test"),
            new("merchant_id", merchantId),
            new("scope", "ledger.write consolidation.read"),
            new("aud", LedgerAudience),
            new("aud", ConsolidationAudience)
        };
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: null,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void ConfigureTestAuthentication(IWebHostBuilder builder, string audience)
    {
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.MetadataAddress = string.Empty;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                options.TokenValidationParameters.IssuerSigningKey = TestSigningKey;
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.ValidAudience = audience;
            });
        });
    }

    private sealed class LedgerSystemApiFactory(string connectionString)
        : WebApplicationFactory<LedgerApi::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Environment "Testing": ver comentário equivalente em LedgerApiFactory.
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Ledger", connectionString);
            // UseSetting (não só ConfigureAppConfiguration): ver comentário
            // equivalente em LedgerApiFactory - confirmado por execução real.
            builder.UseSetting("SecretsManager:Enabled", "false");
            builder.UseSetting("SystemsManager:Enabled", "false");
            builder.UseSetting("Authentication:Authority", Issuer);
            builder.UseSetting("Authentication:Audience", LedgerAudience);
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Ledger"] = connectionString,
                    ["Authentication:Authority"] = Issuer,
                    ["Authentication:Audience"] = LedgerAudience,
                    // Bypass explícito do Secrets Manager/SSM (ADR-0009/ADR-0009):
                    // este teste já fornece ConnectionStrings:Ledger completa
                    // (Testcontainers) e Authentication:Authority/Audience.
                    ["SecretsManager:Enabled"] = "false",
                    ["SystemsManager:Enabled"] = "false"
                });
            });
            ConfigureTestAuthentication(builder, LedgerAudience);
        }
    }

    private sealed class ConsolidationSystemApiFactory(string connectionString)
        : WebApplicationFactory<Program>
    {
        public string ConnectionString => connectionString;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Environment "Testing": ver comentário equivalente em LedgerApiFactory.
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Consolidation", connectionString);
            // UseSetting (não só ConfigureAppConfiguration): ver comentário
            // equivalente em LedgerApiFactory - confirmado por execução real.
            builder.UseSetting("SecretsManager:Enabled", "false");
            builder.UseSetting("SystemsManager:Enabled", "false");
            builder.UseSetting("Authentication:Authority", Issuer);
            builder.UseSetting("Authentication:Audience", ConsolidationAudience);
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Consolidation"] = connectionString,
                    ["Authentication:Authority"] = Issuer,
                    ["Authentication:Audience"] = ConsolidationAudience,
                    // Bypass explícito do Secrets Manager/SSM (ADR-0009/ADR-0009):
                    // este teste já fornece ConnectionStrings:Consolidation completa
                    // (Testcontainers) e Authentication:Authority/Audience.
                    ["SecretsManager:Enabled"] = "false",
                    ["SystemsManager:Enabled"] = "false"
                });
            });
            ConfigureTestAuthentication(builder, ConsolidationAudience);
        }
    }

    private sealed class CapturingApplyFinancialEntryUseCase(string connectionString) : IApplyFinancialEntryUseCase
    {
        public Exception? LastError { get; private set; }

        public async Task<ProjectionResult> ApplyAsync(
            ApplyFinancialEntryCommand command,
            CancellationToken cancellationToken)
        {
            try
            {
                var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
                    .UseNpgsql(connectionString)
                    .Options;
                await using var dbContext = new ConsolidationDbContext(options);
                var useCase = new ApplyFinancialEntryUseCase(
                    new EfDailyBalanceProjectionStore(dbContext),
                    TimeProvider.System);

                return await useCase.ApplyAsync(command, cancellationToken);
            }
            catch (Exception exception)
            {
                LastError = exception;
                throw;
            }
        }
    }
}

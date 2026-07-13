using BancoCarrefour.Ledger.Persistence;
using BancoCarrefour.Ledger.Persistence.Entities;
using Json.Schema;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace BancoCarrefour.Ledger.IntegrationTests;

public sealed class PostEntriesTests : IClassFixture<LedgerApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LedgerApiFactory factory;

    public PostEntriesTests(LedgerApiFactory factory)
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
    public async Task Post_entries_sem_token_retorna_401()
    {
        using var client = factory.CreateClient();

        var response = await PostEntryAsync(client, CreateValidRequest(), "idem-0001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorResponseAsync(response, "AUTHENTICATION_ERROR");
    }

    [Fact]
    public async Task Post_entries_com_token_expirado_retorna_401()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokens.CreateToken("merchant-001", expires: DateTime.UtcNow.AddMinutes(-5)));

        var response = await PostEntryAsync(client, CreateValidRequest(), "idem-0001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorResponseAsync(response, "AUTHENTICATION_ERROR");
    }

    [Fact]
    public async Task Post_entries_com_issuer_invalido_retorna_401()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokens.CreateToken("merchant-001", issuer: "issuer-invalido"));

        var response = await PostEntryAsync(client, CreateValidRequest(), "idem-0001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorResponseAsync(response, "AUTHENTICATION_ERROR");
    }

    [Fact]
    public async Task Post_entries_com_audience_invalida_retorna_401()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokens.CreateToken("merchant-001", audience: "audience-invalida"));

        var response = await PostEntryAsync(client, CreateValidRequest(), "idem-0001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorResponseAsync(response, "AUTHENTICATION_ERROR");
    }

    [Fact]
    public async Task Post_entries_com_token_sem_merchant_id_retorna_403()
    {
        using var client = CreateClientWithToken(null);

        var response = await PostEntryAsync(client, CreateValidRequest(), "idem-0001");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertErrorResponseAsync(response, "AUTHORIZATION_ERROR");
    }

    [Fact]
    public async Task Post_entries_com_merchantId_no_payload_retorna_400()
    {
        using var client = CreateClientWithToken("merchant-001");
        var payload = """
            {
              "type": "CREDIT",
              "amount": "150.75",
              "currency": "BRL",
              "occurredAt": "2026-07-11T13:45:00Z",
              "merchantId": "payload-merchant"
            }
            """;

        var response = await PostEntryAsync(client, payload, "idem-0001");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Post_entries_com_json_invalido_retorna_400_com_error_response()
    {
        using var client = CreateClientWithToken("merchant-001");

        var response = await PostEntryAsync(client, """{"type":"CREDIT","amount":""", "idem-0001");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Post_entries_sem_idempotency_key_retorna_400()
    {
        using var client = CreateClientWithToken("merchant-001");

        var response = await PostEntryAsync(client, CreateValidRequest(), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData("""{"type":"INVALID","amount":"150.75","currency":"BRL","occurredAt":"2026-07-11T13:45:00Z"}""")]
    [InlineData("""{"type":"credit","amount":"150.75","currency":"BRL","occurredAt":"2026-07-11T13:45:00Z"}""")]
    [InlineData("""{"type":"Credit","amount":"150.75","currency":"BRL","occurredAt":"2026-07-11T13:45:00Z"}""")]
    [InlineData("""{"type":"CREDIT","amount":"0.00","currency":"BRL","occurredAt":"2026-07-11T13:45:00Z"}""")]
    [InlineData("""{"type":"CREDIT","amount":"abc","currency":"BRL","occurredAt":"2026-07-11T13:45:00Z"}""")]
    [InlineData("""{"type":"CREDIT","amount":"150.75","currency":"USD","occurredAt":"2026-07-11T13:45:00Z"}""")]
    [InlineData("""{"type":"CREDIT","amount":"150.75","currency":"brl","occurredAt":"2026-07-11T13:45:00Z"}""")]
    [InlineData("""{"type":"CREDIT","amount":"150.75","currency":"Brl","occurredAt":"2026-07-11T13:45:00Z"}""")]
    public async Task Post_entries_com_payload_invalido_retorna_422(string payload)
    {
        using var client = CreateClientWithToken("merchant-001");

        var response = await PostEntryAsync(client, payload, "idem-0001");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorResponseAsync(response, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Post_entries_com_description_acima_de_256_retorna_422()
    {
        using var client = CreateClientWithToken("merchant-001");
        var request = CreateValidRequest() with
        {
            Description = new string('a', 257)
        };

        var response = await PostEntryAsync(client, request, "idem-0001");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorResponseAsync(response, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Post_entries_valido_retorna_201_e_merchantId_do_token()
    {
        using var client = CreateClientWithToken("merchant-token");

        var response = await PostEntryAsync(client, CreateValidRequest(), "idem-0001");
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("merchant-token", body.RootElement.GetProperty("merchantId").GetString());
        Assert.Equal("CREDIT", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("150.75", body.RootElement.GetProperty("amount").GetString());
    }

    [Fact]
    public async Task Post_entries_calcula_businessDate_em_America_Sao_Paulo()
    {
        using var client = CreateClientWithToken("merchant-001");
        var request = CreateValidRequest() with
        {
            OccurredAt = DateTimeOffset.Parse("2026-07-12T02:30:00Z")
        };

        var response = await PostEntryAsync(client, request, "idem-0001");
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("2026-07-11", body.RootElement.GetProperty("businessDate").GetString());
    }

    [Fact]
    public async Task Post_entries_valido_cria_entry_input_idempotency_e_outbox_pending()
    {
        using var client = CreateClientWithToken("merchant-001");

        var response = await PostEntryAsync(client, CreateValidRequest(), "idem-0001");
        var body = await ReadJsonAsync(response);
        var entryId = body.RootElement.GetProperty("entryId").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var entry = await dbContext.Entries.AsNoTracking().SingleAsync(x => x.EntryId == entryId);
        var inputIdempotency = await dbContext.InputIdempotencyRecords.AsNoTracking().SingleAsync();
        var outbox = await dbContext.OutboxMessages.AsNoTracking().SingleAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("merchant-001", entry.MerchantId);
        Assert.Equal(entryId, inputIdempotency.EntryId);
        Assert.Equal("idem-0001", inputIdempotency.IdempotencyKey);
        Assert.Equal(OutboxMessageStatus.Pending, outbox.Status);
    }

    [Fact]
    public async Task Post_entries_valido_cria_payload_outbox_compativel_com_entry_created_v1()
    {
        using var client = CreateClientWithToken("merchant-001");

        var response = await PostEntryAsync(client, CreateValidRequest(), "idem-0001");
        var body = await ReadJsonAsync(response);
        var entryId = body.RootElement.GetProperty("entryId").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var outbox = await dbContext.OutboxMessages.AsNoTracking().SingleAsync();
        var payload = JsonNode.Parse(outbox.Payload);
        var schema = LoadEntryCreatedSchema();
        var result = schema.Evaluate(payload, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(result.IsValid);
        Assert.Equal(entryId.ToString(), payload?["entryId"]?.GetValue<string>());
        Assert.Equal("EntryCreated", payload?["eventType"]?.GetValue<string>());
        Assert.Equal(1, payload?["eventVersion"]?.GetValue<int>());
        Assert.Equal("merchant-001", payload?["merchantId"]?.GetValue<string>());
        Assert.Equal("corr-test", payload?["correlationId"]?.GetValue<string>());
        Assert.Equal("150.75", payload?["amount"]?.GetValue<string>());
    }

    [Fact]
    public async Task Post_entries_replay_mesma_chave_e_fingerprint_retorna_200_e_mesmo_entryId()
    {
        using var client = CreateClientWithToken("merchant-001");

        var first = await PostEntryAsync(client, CreateValidRequest(), "idem-0001");
        var firstBody = await ReadJsonAsync(first);
        var replay = await PostEntryAsync(client, CreateValidRequest(), "idem-0001");
        var replayBody = await ReadJsonAsync(replay);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(firstBody.RootElement.GetProperty("entryId").GetGuid(), replayBody.RootElement.GetProperty("entryId").GetGuid());
    }

    [Fact]
    public async Task Post_entries_concorrente_mesma_chave_e_payload_equivalente_cria_um_lancamento_e_replays_consistentes()
    {
        using var client = CreateClientWithToken("merchant-concurrent");
        var payload = CreateValidRequest();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8)
            .Select(async index =>
            {
                await gate.Task;

                return await PostEntryAsync(client, payload, "idem-concurrent-001", $"corr-concurrent-{index}");
            })
            .ToArray();

        gate.SetResult();
        var responses = await Task.WhenAll(tasks);
        var entryIds = new List<Guid>();

        foreach (var response in responses.Where(x => x.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK))
        {
            using var body = await ReadJsonAsync(response);
            entryIds.Add(body.RootElement.GetProperty("entryId").GetGuid());
            Assert.Equal("merchant-concurrent", body.RootElement.GetProperty("merchantId").GetString());
            Assert.Equal("idem-concurrent-001", body.RootElement.GetProperty("idempotencyKey").GetString());
        }

        var counts = await CountLedgerRecordsAsync();

        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Single(entryIds.Distinct());
        Assert.Equal((1, 1, 1), counts);
    }

    [Fact]
    public async Task Post_entries_mesma_chave_e_payload_divergente_retorna_409()
    {
        using var client = CreateClientWithToken("merchant-001");
        var divergent = CreateValidRequest() with
        {
            Amount = "151.75"
        };

        var first = await PostEntryAsync(client, CreateValidRequest(), "idem-0001");
        var conflict = await PostEntryAsync(client, divergent, "idem-0001");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertErrorResponseAsync(conflict, "IDEMPOTENCY_CONFLICT");
    }

    [Fact]
    public async Task Post_entries_concorrente_mesma_chave_e_payload_divergente_cria_um_lancamento_e_rejeita_divergente()
    {
        using var client = CreateClientWithToken("merchant-concurrent");
        var divergent = CreateValidRequest() with
        {
            Amount = "151.75"
        };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new[]
        {
            Task.Run(async () =>
            {
                await gate.Task;

                return await PostEntryAsync(client, CreateValidRequest(), "idem-concurrent-002", "corr-concurrent-valid");
            }),
            Task.Run(async () =>
            {
                await gate.Task;

                return await PostEntryAsync(client, divergent, "idem-concurrent-002", "corr-concurrent-divergent");
            })
        };

        gate.SetResult();
        var responses = await Task.WhenAll(tasks);
        var counts = await CountLedgerRecordsAsync();
        var conflict = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);

        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        await AssertErrorResponseAsync(conflict, "IDEMPOTENCY_CONFLICT", expectedCorrelationId: null);
        Assert.Equal((1, 1, 1), counts);
    }

    [Fact]
    public async Task Post_entries_mesma_chave_para_merchants_diferentes_nao_colide()
    {
        using var firstClient = CreateClientWithToken("merchant-001");
        using var secondClient = CreateClientWithToken("merchant-002");

        var first = await PostEntryAsync(firstClient, CreateValidRequest(), "idem-0001");
        var second = await PostEntryAsync(secondClient, CreateValidRequest(), "idem-0001");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Post_entries_com_merchant_id_maior_que_limite_retorna_403_e_nao_persiste()
    {
        using var client = CreateClientWithToken(new string('m', 65));

        var response = await PostEntryAsync(client, CreateValidRequest(), "idem-0001");
        var counts = await CountLedgerRecordsAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertErrorResponseAsync(response, "AUTHORIZATION_ERROR");
        Assert.Equal((0, 0, 0), counts);
    }

    [Fact]
    public async Task Post_entries_com_correlation_id_maior_que_limite_retorna_400_e_nao_persiste()
    {
        using var client = CreateClientWithToken("merchant-001");

        var response = await PostEntryAsync(client, CreateValidRequest(), "idem-0001", new string('c', 129));
        var counts = await CountLedgerRecordsAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "VALIDATION_ERROR", expectedCorrelationId: null);
        Assert.Equal((0, 0, 0), counts);
    }

    [Fact]
    public async Task Post_entries_com_banco_indisponivel_retorna_503_com_error_response()
    {
        using var unavailableFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(service => service.ServiceType == typeof(DbContextOptions<LedgerDbContext>));
                services.Remove(descriptor);
                services.AddDbContext<LedgerDbContext>(options =>
                    options.UseNpgsql("Host=127.0.0.1;Port=1;Database=ledger;Username=ledger;Password=ledger;Timeout=1;Command Timeout=1"));
            });
        });
        using var client = unavailableFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtTokens.CreateToken("merchant-001"));

        var response = await PostEntryAsync(client, CreateValidRequest(), "idem-0001");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertErrorResponseAsync(response, "SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task Post_entries_excede_rate_limit_configurado_retorna_429_com_error_response()
    {
        using var rateLimitedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimit:PermitLimit", "1");
            builder.UseSetting("RateLimit:WindowSeconds", "60");
        });
        using var client = rateLimitedFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtTokens.CreateToken("merchant-rate-limit"));

        var first = await PostEntryAsync(client, CreateValidRequest(), "idem-rate-001", "corr-rate-1");
        var second = await PostEntryAsync(client, CreateValidRequest(), "idem-rate-002", "corr-rate-2");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        await AssertErrorResponseAsync(second, "RATE_LIMIT_EXCEEDED", "corr-rate-2");
    }

    private HttpClient CreateClientWithToken(string? merchantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtTokens.CreateToken(merchantId));

        return client;
    }

    private static CreateEntryTestRequest CreateValidRequest()
    {
        return new CreateEntryTestRequest(
            "CREDIT",
            "150.75",
            "BRL",
            DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            "Venda cartão");
    }

    private async Task<(int Entries, int InputIdempotency, int Outbox)> CountLedgerRecordsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

        return (
            await dbContext.Entries.CountAsync(),
            await dbContext.InputIdempotencyRecords.CountAsync(),
            await dbContext.OutboxMessages.CountAsync());
    }

    private static Task<HttpResponseMessage> PostEntryAsync(
        HttpClient client,
        object payload,
        string? idempotencyKey,
        string? correlationId = "corr-test")
    {
        var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        return SendEntryRequestAsync(client, content, idempotencyKey, correlationId);
    }

    private static Task<HttpResponseMessage> PostEntryAsync(
        HttpClient client,
        string payload,
        string? idempotencyKey,
        string? correlationId = "corr-test")
    {
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        return SendEntryRequestAsync(client, content, idempotencyKey, correlationId);
    }

    private static async Task<HttpResponseMessage> SendEntryRequestAsync(
        HttpClient client,
        HttpContent content,
        string? idempotencyKey,
        string? correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/entries")
        {
            Content = content
        };

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-Id", correlationId);
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();

        return await JsonDocument.ParseAsync(stream);
    }

    private static async Task<JsonDocument> AssertErrorResponseAsync(
        HttpResponseMessage response,
        string expectedErrorCode,
        string? expectedCorrelationId = "corr-test")
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await ReadJsonAsync(response);
        var root = body.RootElement;

        Assert.True(root.TryGetProperty("errorCode", out var errorCode));
        Assert.True(root.TryGetProperty("message", out var message));
        Assert.True(root.TryGetProperty("correlationId", out var correlationId));
        Assert.Equal(expectedErrorCode, errorCode.GetString());
        Assert.False(string.IsNullOrWhiteSpace(message.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(correlationId.GetString()));
        Assert.True(correlationId.GetString()!.Length <= 128);

        if (expectedCorrelationId is not null)
        {
            Assert.Equal(expectedCorrelationId, correlationId.GetString());
        }

        return body;
    }

    private static JsonSchema LoadEntryCreatedSchema()
    {
        var path = Path.Combine(RepositoryRootPath, "contracts", "events", "entry-created-v1.schema.json");

        return JsonSchema.FromText(File.ReadAllText(path));
    }

    private static string RepositoryRootPath { get; } = LocateRepositoryRoot();

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var schemaPath = Path.Combine(directory.FullName, "contracts", "events", "entry-created-v1.schema.json");

            if (File.Exists(schemaPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Não foi possível localizar a raiz do repositório.");
    }

    private sealed record CreateEntryTestRequest(
        string Type,
        string Amount,
        string Currency,
        DateTimeOffset OccurredAt,
        string? Description);
}

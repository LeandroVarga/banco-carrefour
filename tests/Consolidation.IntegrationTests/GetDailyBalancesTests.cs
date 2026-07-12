using BancoCarrefour.Consolidation.Persistence;
using BancoCarrefour.Consolidation.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

namespace BancoCarrefour.Consolidation.IntegrationTests;

public sealed class GetDailyBalancesTests : IClassFixture<ConsolidationApiFactory>, IAsyncLifetime
{
    private readonly ConsolidationApiFactory factory;

    public GetDailyBalancesTests(ConsolidationApiFactory factory)
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
    public async Task Get_dailyBalance_sem_token_retorna_401_com_errorResponse()
    {
        using var client = factory.CreateClient();

        var response = await GetDailyBalanceAsync(client, "2026-07-11");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorResponseAsync(response, "AUTHENTICATION_ERROR");
    }

    [Fact]
    public async Task Get_dailyBalance_com_token_sem_merchant_id_retorna_403_com_errorResponse()
    {
        using var client = CreateClientWithToken(null);

        var response = await GetDailyBalanceAsync(client, "2026-07-11");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertErrorResponseAsync(response, "AUTHORIZATION_ERROR");
    }

    [Fact]
    public async Task Get_dailyBalance_com_merchant_id_maior_que_limite_retorna_403_com_errorResponse()
    {
        using var client = CreateClientWithToken(new string('m', 65));

        var response = await GetDailyBalanceAsync(client, "2026-07-11");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertErrorResponseAsync(response, "AUTHORIZATION_ERROR");
    }

    [Fact]
    public async Task Get_dailyBalance_com_businessDate_invalido_retorna_400_com_errorResponse()
    {
        using var client = CreateClientWithToken("merchant-001");

        var response = await GetDailyBalanceAsync(client, "11-07-2026");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Get_dailyBalance_com_correlation_id_maior_que_limite_retorna_400_com_errorResponse()
    {
        using var client = CreateClientWithToken("merchant-001");

        var response = await GetDailyBalanceAsync(client, "2026-07-11", new string('c', 129));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "VALIDATION_ERROR", expectedCorrelationId: null);
    }

    [Fact]
    public async Task Get_dailyBalance_existente_retorna_200_com_valores_corretos()
    {
        await InsertDailyBalanceAsync("merchant-001", new DateOnly(2026, 7, 11));
        using var client = CreateClientWithToken("merchant-001");

        var response = await GetDailyBalanceAsync(client, "2026-07-11");
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("merchant-001", body.RootElement.GetProperty("merchantId").GetString());
        Assert.Equal("2026-07-11", body.RootElement.GetProperty("businessDate").GetString());
        Assert.Equal("150.70", body.RootElement.GetProperty("totalCredits").GetString());
        Assert.Equal("25.10", body.RootElement.GetProperty("totalDebits").GetString());
        Assert.Equal("125.60", body.RootElement.GetProperty("balance").GetString());
        Assert.Equal("BRL", body.RootElement.GetProperty("currency").GetString());
        Assert.Equal(2, body.RootElement.GetProperty("entriesCount").GetInt64());
        Assert.Equal(DateTimeOffset.Parse("2026-07-11T13:45:05Z"), body.RootElement.GetProperty("lastUpdatedAt").GetDateTimeOffset());
        Assert.False(body.RootElement.TryGetProperty("lastEventOccurredAt", out _));
    }

    [Fact]
    public async Task Get_dailyBalance_nao_permite_acessar_saldo_de_outro_merchant()
    {
        await InsertDailyBalanceAsync("merchant-002", new DateOnly(2026, 7, 11));
        using var client = CreateClientWithToken("merchant-001");

        var response = await GetDailyBalanceAsync(client, "2026-07-11");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertErrorResponseAsync(response, "DAILY_BALANCE_NOT_FOUND");
    }

    [Fact]
    public async Task Get_dailyBalance_inexistente_retorna_404_sem_confirmar_saldo_zero()
    {
        using var client = CreateClientWithToken("merchant-001");

        var response = await GetDailyBalanceAsync(client, "2026-07-11");
        var body = await AssertErrorResponseAsync(response, "DAILY_BALANCE_NOT_FOUND");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("não confirma saldo zero", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Get_dailyBalance_com_banco_indisponivel_retorna_503_com_errorResponse()
    {
        using var unavailableFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(service => service.ServiceType == typeof(DbContextOptions<ConsolidationDbContext>));
                services.Remove(descriptor);
                services.AddDbContext<ConsolidationDbContext>(options =>
                    options.UseNpgsql("Host=127.0.0.1;Port=1;Database=consolidation;Username=consolidation;Password=consolidation;Timeout=1;Command Timeout=1"));
            });
        });
        using var client = unavailableFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ConsolidationTestJwtTokens.CreateToken("merchant-001"));

        var response = await GetDailyBalanceAsync(client, "2026-07-11");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertErrorResponseAsync(response, "SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task Get_dailyBalance_excede_rate_limit_configurado_retorna_429_com_errorResponse()
    {
        await InsertDailyBalanceAsync("merchant-rate-limit", new DateOnly(2026, 7, 11));
        using var rateLimitedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimit:PermitLimit", "1");
            builder.UseSetting("RateLimit:WindowSeconds", "60");
        });
        using var client = rateLimitedFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ConsolidationTestJwtTokens.CreateToken("merchant-rate-limit"));

        var first = await GetDailyBalanceAsync(client, "2026-07-11", "corr-rate-1");
        var second = await GetDailyBalanceAsync(client, "2026-07-11", "corr-rate-2");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        await AssertErrorResponseAsync(second, "RATE_LIMIT_EXCEEDED", "corr-rate-2");
    }

    private HttpClient CreateClientWithToken(string? merchantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ConsolidationTestJwtTokens.CreateToken(merchantId));

        return client;
    }

    private static async Task<HttpResponseMessage> GetDailyBalanceAsync(
        HttpClient client,
        string businessDate,
        string? correlationId = "corr-test")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/daily-balances/{businessDate}");

        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-Id", correlationId);
        }

        return await client.SendAsync(request);
    }

    private async Task InsertDailyBalanceAsync(string merchantId, DateOnly businessDate)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();

        dbContext.DailyBalances.Add(new DailyBalance
        {
            DailyBalanceId = Guid.NewGuid(),
            MerchantId = merchantId,
            BusinessDate = businessDate,
            TotalCredits = 150.7m,
            TotalDebits = 25.1m,
            Balance = 125.6m,
            Currency = "BRL",
            EntryCount = 2,
            LastEventOccurredAt = DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            LastUpdatedAt = DateTimeOffset.Parse("2026-07-11T13:45:05Z")
        });

        await dbContext.SaveChangesAsync();
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
}

using BancoCarrefour.Ledger.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using Xunit;

namespace BancoCarrefour.Ledger.IntegrationTests;

public sealed class HealthEndpointsTests : IClassFixture<LedgerApiFactory>
{
    private readonly LedgerApiFactory factory;

    public HealthEndpointsTests(LedgerApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Health_live_retorna_200_sem_autenticacao()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("self", body.RootElement.GetProperty("checks")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Health_ready_retorna_200_com_banco_disponivel_sem_autenticacao()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("ledger-postgres", body.RootElement.GetProperty("checks")[0].GetProperty("name").GetString());
        Assert.Equal("Healthy", body.RootElement.GetProperty("checks")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Health_ready_retorna_503_com_banco_indisponivel_sem_expor_detalhes()
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

        var response = await client.GetAsync("/health/ready");
        var bodyText = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(bodyText);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("ledger-postgres", body.RootElement.GetProperty("checks")[0].GetProperty("name").GetString());
        Assert.Equal("Unhealthy", body.RootElement.GetProperty("checks")[0].GetProperty("status").GetString());
        Assert.DoesNotContain("Host=", bodyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", bodyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", bodyText, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var stream = await response.Content.ReadAsStreamAsync();

        return await JsonDocument.ParseAsync(stream);
    }
}

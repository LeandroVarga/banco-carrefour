using BancoCarrefour.Consolidation.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BancoCarrefour.Consolidation.IntegrationTests;

public sealed class ConsolidationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "ledger-local-development-signing-key-32-bytes";
    public const string Issuer = "banco-carrefour-local";
    public const string Audience = "banco-carrefour-api";
    private readonly ConsolidationTestDatabase database = new();

    public string ConnectionString => database.ConnectionString;

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();

        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();

        await dbContext.Database.MigrateAsync();
        await ResetDatabaseAsync();
    }

    public new async Task DisposeAsync()
    {
        Dispose();
        await database.DisposeAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();

        await dbContext.ProcessedEvents.ExecuteDeleteAsync();
        await dbContext.DailyBalances.ExecuteDeleteAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Consolidation"] = ConnectionString,
                ["Authentication:SigningKey"] = SigningKey,
                ["Authentication:Issuer"] = Issuer,
                ["Authentication:Audience"] = Audience
            });
        });
    }
}

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

    public string ConnectionString { get; } = Environment.GetEnvironmentVariable("CONSOLIDATION_TEST_CONNECTION_STRING")
        ?? "Host=consolidation-postgres;Port=5432;Database=consolidation;Username=consolidation;Password=consolidation";

    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();

        await dbContext.Database.MigrateAsync();
        await ResetDatabaseAsync();
    }

    public new Task DisposeAsync()
    {
        return Task.CompletedTask;
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
                ["Authentication:SigningKey"] = SigningKey
            });
        });
    }
}

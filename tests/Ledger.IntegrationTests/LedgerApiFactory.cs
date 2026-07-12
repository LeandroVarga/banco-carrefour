using BancoCarrefour.Ledger.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BancoCarrefour.Ledger.IntegrationTests;

public sealed class LedgerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "ledger-local-development-signing-key-32-bytes";
    public const string Issuer = "banco-carrefour-local";
    public const string Audience = "banco-carrefour-api";

    public string ConnectionString { get; } = Environment.GetEnvironmentVariable("LEDGER_TEST_CONNECTION_STRING")
        ?? "Host=ledger-postgres;Port=5432;Database=ledger;Username=ledger;Password=ledger";

    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

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
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

        await dbContext.OutboxMessages.ExecuteDeleteAsync();
        await dbContext.InputIdempotencyRecords.ExecuteDeleteAsync();
        await dbContext.Entries.ExecuteDeleteAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Ledger"] = ConnectionString,
                ["Authentication:SigningKey"] = SigningKey,
                ["Authentication:Issuer"] = Issuer,
                ["Authentication:Audience"] = Audience
            });
        });
    }
}

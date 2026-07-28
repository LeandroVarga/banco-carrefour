using BancoCarrefour.Ledger.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace BancoCarrefour.Ledger.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class LedgerIntegrationCollection : ICollectionFixture<LedgerIntegrationTestFixture>
{
    public const string Name = "LedgerIntegration";
}

public sealed class LedgerIntegrationTestFixture : IAsyncLifetime
{
    // WithTmpfsMount evita o volume anônimo que a própria imagem postgres
    // declara para /var/lib/postgresql/data (Config.Volumes, confirmado via
    // "docker image inspect") - sem esse mount explícito, Docker cria um
    // volume anônimo por container mesmo com WithCleanUp(true)/DisposeAsync
    // corretos, porque a remoção do container não implica remoção do volume
    // (auditoria de capacidade ). Dado local de teste, descartável
    // a cada execução (nenhuma persistência é esperada entre testes).
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("ledger")
        .WithUsername("ledger")
        .WithPassword("ledger")
        .WithTmpfsMount("/var/lib/postgresql/data")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();

        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        await ResetDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await postgres.DisposeAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        await using var dbContext = CreateContext();

        await dbContext.OutboxMessages.ExecuteDeleteAsync();
        await dbContext.InputIdempotencyRecords.ExecuteDeleteAsync();
        await dbContext.Entries.ExecuteDeleteAsync();
    }

    private LedgerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new LedgerDbContext(options);
    }
}

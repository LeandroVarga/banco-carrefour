using Testcontainers.PostgreSql;
using Xunit;

namespace BancoCarrefour.MigrationRunner.Tests;

[CollectionDefinition(Name)]
public sealed class PostgresLockTestCollection : ICollectionFixture<PostgresLockTestFixture>
{
    public const string Name = "PostgresLock";
}

public sealed class PostgresLockTestFixture : IAsyncLifetime
{
    // WithTmpfsMount pelo mesmo motivo documentado em
    // LedgerIntegrationTestFixture (Ledger.IntegrationTests) - evita volume
    // anônimo residual da própria imagem postgres.
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("migration_lock_tests")
        .WithUsername("migration_lock_tests")
        .WithPassword("migration_lock_tests")
        .WithTmpfsMount("/var/lib/postgresql/data")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => postgres.GetConnectionString();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();
}

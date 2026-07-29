using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.Database;

[CollectionDefinition(Name)]
public sealed class DatabasePrivilegesCollection : ICollectionFixture<DatabasePrivilegesFixture>
{
    public const string Name = "DatabasePrivileges";
}

/// <summary>
/// Sobe Postgres real (mesma imagem base do Compose) e executa os SCRIPTS
/// REAIS de menor privilégio (<c>infra/postgres/ledger/*.sql</c>,
/// <c>infra/postgres/consolidation/*.sql</c>) via <c>psql</c> dentro do
/// container — nunca uma reimplementação paralela dos grants. Prova, com
/// conexões reais por role, que o menor privilégio funciona (ver ADR-0009).
/// </summary>
public sealed class DatabasePrivilegesFixture : IAsyncLifetime
{
    private const string LedgerMigrationPassword = "ledger-migration-test-pw";
    private const string LedgerApiPassword = "ledger-api-test-pw";
    private const string LedgerOutboxPublisherPassword = "ledger-outbox-publisher-test-pw";
    private const string ConsolidationMigrationPassword = "consolidation-migration-test-pw";
    private const string ConsolidationWorkerPassword = "consolidation-worker-test-pw";
    private const string ConsolidationApiReadonlyPassword = "consolidation-api-readonly-test-pw";

    private PostgreSqlContainer ledgerPostgres = null!;
    private PostgreSqlContainer consolidationPostgres = null!;

    public string BuildLedgerConnectionString(string username, string password)
    {
        return new NpgsqlConnectionStringBuilder(ledgerPostgres.GetConnectionString())
        {
            Username = username,
            Password = password
        }.ConnectionString;
    }

    public string BuildConsolidationConnectionString(string username, string password)
    {
        return new NpgsqlConnectionStringBuilder(consolidationPostgres.GetConnectionString())
        {
            Username = username,
            Password = password
        }.ConnectionString;
    }

    public string LedgerMigrationConnectionString => BuildLedgerConnectionString("ledger_migration", LedgerMigrationPassword);
    public string LedgerApiConnectionString => BuildLedgerConnectionString("ledger_api", LedgerApiPassword);
    public string LedgerOutboxPublisherConnectionString => BuildLedgerConnectionString("ledger_outbox_publisher", LedgerOutboxPublisherPassword);
    public string ConsolidationWorkerConnectionString => BuildConsolidationConnectionString("consolidation_worker", ConsolidationWorkerPassword);
    public string ConsolidationApiReadonlyConnectionString => BuildConsolidationConnectionString("consolidation_api_readonly", ConsolidationApiReadonlyPassword);

    public async Task InitializeAsync()
    {
        var repositoryRoot = LocateRepositoryRoot();

        // WithResourceMapping(string, string) — o overload de caminho de
        // host — cria o destino como diretório em vez de arquivo nesta
        // versão do Testcontainers (mesmo achado já documentado em
        // IdentityFixture/EdgeFixture); usa-se o overload byte[] com o
        // conteúdo real do arquivo lido do disco.
        var ledgerRoleBootstrapSql = await File.ReadAllBytesAsync(
            Path.Combine(repositoryRoot, "infra", "postgres", "ledger", "db-role-bootstrap.sql"));
        var ledgerGrantsBootstrapSql = await File.ReadAllBytesAsync(
            Path.Combine(repositoryRoot, "infra", "postgres", "ledger", "db-grants-bootstrap.sql"));
        var consolidationRoleBootstrapSql = await File.ReadAllBytesAsync(
            Path.Combine(repositoryRoot, "infra", "postgres", "consolidation", "db-role-bootstrap.sql"));
        var consolidationGrantsBootstrapSql = await File.ReadAllBytesAsync(
            Path.Combine(repositoryRoot, "infra", "postgres", "consolidation", "db-grants-bootstrap.sql"));

        // WithTmpfsMount evita o volume anônimo que a imagem postgres declara
        // para /var/lib/postgresql/data - ver comentário completo em
        // LedgerIntegrationCollection.cs.
        ledgerPostgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("ledger")
            .WithUsername("ledger")
            .WithPassword("ledger")
            .WithResourceMapping(ledgerRoleBootstrapSql, "/sql/db-role-bootstrap.sql")
            .WithResourceMapping(ledgerGrantsBootstrapSql, "/sql/db-grants-bootstrap.sql")
            .WithTmpfsMount("/var/lib/postgresql/data")
            .WithCleanUp(true)
            .Build();

        consolidationPostgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("consolidation")
            .WithUsername("consolidation")
            .WithPassword("consolidation")
            .WithResourceMapping(consolidationRoleBootstrapSql, "/sql/db-role-bootstrap.sql")
            .WithResourceMapping(consolidationGrantsBootstrapSql, "/sql/db-grants-bootstrap.sql")
            .WithTmpfsMount("/var/lib/postgresql/data")
            .WithCleanUp(true)
            .Build();

        await Task.WhenAll(ledgerPostgres.StartAsync(), consolidationPostgres.StartAsync());

        await RunLedgerRoleBootstrapAsync();
        await MigrateLedgerAsSelfAsync();
        await RunLedgerGrantsBootstrapAsync();

        await RunConsolidationRoleBootstrapAsync();
        await MigrateConsolidationAsSelfAsync();
        await RunConsolidationGrantsBootstrapAsync();
    }

    private async Task RunLedgerRoleBootstrapAsync()
    {
        var result = await ledgerPostgres.ExecAsync(
        [
            "psql", "-U", "ledger", "-d", "ledger",
            "-v", "ON_ERROR_STOP=1",
            "-v", $"ledger_migration_password={LedgerMigrationPassword}",
            "-v", $"ledger_api_password={LedgerApiPassword}",
            "-v", $"ledger_outbox_publisher_password={LedgerOutboxPublisherPassword}",
            "-f", "/sql/db-role-bootstrap.sql"
        ]);

        Assert.True(result.ExitCode == 0, $"db-role-bootstrap (Ledger) falhou: {result.Stderr}");
    }

    private async Task RunLedgerGrantsBootstrapAsync()
    {
        var result = await ledgerPostgres.ExecAsync(
        [
            "psql", "-U", "ledger_migration", "-d", "ledger",
            "-v", "ON_ERROR_STOP=1",
            "-f", "/sql/db-grants-bootstrap.sql"
        ]);

        Assert.True(result.ExitCode == 0, $"db-grants-bootstrap (Ledger) falhou: {result.Stderr}");
    }

    private async Task RunConsolidationRoleBootstrapAsync()
    {
        var result = await consolidationPostgres.ExecAsync(
        [
            "psql", "-U", "consolidation", "-d", "consolidation",
            "-v", "ON_ERROR_STOP=1",
            "-v", $"consolidation_migration_password={ConsolidationMigrationPassword}",
            "-v", $"consolidation_worker_password={ConsolidationWorkerPassword}",
            "-v", $"consolidation_api_readonly_password={ConsolidationApiReadonlyPassword}",
            "-f", "/sql/db-role-bootstrap.sql"
        ]);

        Assert.True(result.ExitCode == 0, $"db-role-bootstrap (Consolidation) falhou: {result.Stderr}");
    }

    private async Task RunConsolidationGrantsBootstrapAsync()
    {
        var result = await consolidationPostgres.ExecAsync(
        [
            "psql", "-U", "consolidation_migration", "-d", "consolidation",
            "-v", "ON_ERROR_STOP=1",
            "-f", "/sql/db-grants-bootstrap.sql"
        ]);

        Assert.True(result.ExitCode == 0, $"db-grants-bootstrap (Consolidation) falhou: {result.Stderr}");
    }

    private async Task MigrateLedgerAsSelfAsync()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<BancoCarrefour.Ledger.Infrastructure.LedgerDbContext>()
            .UseNpgsql(LedgerMigrationConnectionString)
            .Options;
        await using var dbContext = new BancoCarrefour.Ledger.Infrastructure.LedgerDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    private async Task MigrateConsolidationAsSelfAsync()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<BancoCarrefour.Consolidation.Infrastructure.ConsolidationDbContext>()
            .UseNpgsql(ConsolidationMigrationConnectionString())
            .Options;
        await using var dbContext = new BancoCarrefour.Consolidation.Infrastructure.ConsolidationDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    private string ConsolidationMigrationConnectionString()
    {
        return BuildConsolidationConnectionString("consolidation_migration", ConsolidationMigrationPassword);
    }

    public async Task DisposeAsync()
    {
        await ledgerPostgres.DisposeAsync();
        await consolidationPostgres.DisposeAsync();
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BancoCarrefour.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Não foi possível localizar a raiz do repositório.");
    }
}

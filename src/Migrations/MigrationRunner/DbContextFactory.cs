using BancoCarrefour.Consolidation.Infrastructure;
using BancoCarrefour.Consolidation.Infrastructure.Secrets;
using BancoCarrefour.Ledger.Infrastructure;
using BancoCarrefour.Ledger.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BancoCarrefour.MigrationRunner;

/// <summary>
/// Único ponto que sabe construir um DbContext a partir de uma fronteira -
/// reaproveita EXATAMENTE os resolvers de connection string já usados por
/// Ledger.Api/Ledger.OutboxPublisher e Consolidation.Api/Consolidation.Worker
/// (LedgerConnectionStringResolver/ConsolidationConnectionStringResolver),
/// nunca uma resolução de segredo duplicada ou divergente.
/// </summary>
public static class DbContextFactory
{
    // secretNameOverride: a MESMA task definition ECS serve Ledger e
    // Consolidation (ver módulo ecs-migration-task) - qual secret de
    // credenciais de migração usar (ledger-migration vs
    // consolidation-migration) só é conhecido por invocação
    // (--secret-name/--boundary do "aws ecs run-task --overrides"), nunca
    // fixo na task definition. Quando informado, sobrepõe
    // "SecretsManager:SecretName" antes de chamar o resolver existente
    // (o mesmo já usado por Ledger.Api/Consolidation.Api).
    public static string ResolveConnectionString(MigrationBoundary boundary, IConfiguration configuration, string environmentName, string? secretNameOverride = null)
    {
        var effectiveConfiguration = configuration;
        if (!string.IsNullOrWhiteSpace(secretNameOverride))
        {
            effectiveConfiguration = new ConfigurationBuilder()
                .AddConfiguration(configuration)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SecretsManager:SecretName"] = secretNameOverride,
                })
                .Build();
        }

        return boundary switch
        {
            MigrationBoundary.Ledger => LedgerConnectionStringResolver.Resolve(effectiveConfiguration, environmentName),
            MigrationBoundary.Consolidation => ConsolidationConnectionStringResolver.Resolve(effectiveConfiguration, environmentName),
            _ => throw new ArgumentOutOfRangeException(nameof(boundary)),
        };
    }

    // contextOwnsConnection=false: a conexão foi aberta e travada (advisory
    // lock) por PostgresMigrationLock e continua sendo dona/responsável por
    // ela - o DbContext nunca deve fechar essa conexão sozinho, ou o lock de
    // sessão seria liberado antes da migration terminar.
    public static DbContext Create(MigrationBoundary boundary, NpgsqlConnection connection)
    {
        return boundary switch
        {
            MigrationBoundary.Ledger => new LedgerDbContext(
                new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(connection, contextOwnsConnection: false).Options),
            MigrationBoundary.Consolidation => new ConsolidationDbContext(
                new DbContextOptionsBuilder<ConsolidationDbContext>().UseNpgsql(connection, contextOwnsConnection: false).Options),
            _ => throw new ArgumentOutOfRangeException(nameof(boundary)),
        };
    }

    public static System.Reflection.Assembly MigrationsAssembly(MigrationBoundary boundary) => boundary switch
    {
        MigrationBoundary.Ledger => typeof(LedgerDbContext).Assembly,
        MigrationBoundary.Consolidation => typeof(ConsolidationDbContext).Assembly,
        _ => throw new ArgumentOutOfRangeException(nameof(boundary)),
    };
}

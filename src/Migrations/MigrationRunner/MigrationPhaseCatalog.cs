using System.Reflection;
using BancoCarrefour.Contracts.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BancoCarrefour.MigrationRunner;

/// <summary>
/// Mapeia cada migration id (o mesmo id retornado por
/// Database.GetPendingMigrations()) para a fase declarada via
/// [MigrationPhase(...)] - lida por reflexão real sobre o assembly do
/// DbContext, NUNCA inferida do nome do arquivo/classe. O id de cada migration
/// vem do [Migration("...")] padrão do próprio EF Core (gerado no arquivo
/// .Designer.cs), uma API pública e estável - nunca de um serviço interno
/// não documentado do EF Core (ex.: IMigrationsAssembly).
/// </summary>
public static class MigrationPhaseCatalog
{
    public static IReadOnlyDictionary<string, MigrationPhase> Build(Assembly migrationsAssembly)
    {
        var result = new Dictionary<string, MigrationPhase>(StringComparer.Ordinal);

        foreach (var type in migrationsAssembly.GetTypes())
        {
            if (!typeof(Migration).IsAssignableFrom(type) || type.IsAbstract)
            {
                continue;
            }

            var migrationAttribute = type.GetCustomAttribute<MigrationAttribute>()
                ?? throw new InvalidOperationException(
                    $"A migration '{type.FullName}' não tem o atributo [Migration] padrão do EF Core - não é possível determinar seu id.");

            var phaseAttribute = type.GetCustomAttribute<MigrationPhaseAttribute>()
                ?? throw new InvalidOperationException(
                    $"A migration '{type.FullName}' (id '{migrationAttribute.Id}') não tem [MigrationPhase(...)] - " +
                    "toda migration real deste repositório precisa declarar sua fase explicitamente (EXPAND, BACKFILL ou CONTRACT).");

            result[migrationAttribute.Id] = phaseAttribute.Phase;
        }

        return result;
    }
}

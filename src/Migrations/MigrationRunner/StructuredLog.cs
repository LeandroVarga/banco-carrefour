using System.Text.Json;

namespace BancoCarrefour.MigrationRunner;

/// <summary>
/// Log estruturado (uma linha JSON por evento) em stdout - nunca inclui
/// connection string, senha, ou qualquer valor de secret. Os campos aqui
/// (evento, ambiente, fronteira, release, commit, fase, duração, resultado
/// do lock) são exatamente os exigidos pela governança de observabilidade
/// de migração (ver ADR-0015) - sem um framework de logging externo, para manter este
/// executável mínimo e com nenhuma dependência que possa vazar dados por
/// engano (ex.: um enricher de terceiros).
/// </summary>
public static class StructuredLog
{
    public static void Emit(string eventName, IReadOnlyDictionary<string, object?> fields)
    {
        var payload = new Dictionary<string, object?>(fields)
        {
            ["event"] = eventName,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
        };

        Console.WriteLine(JsonSerializer.Serialize(payload));
    }
}

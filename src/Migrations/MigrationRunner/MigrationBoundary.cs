namespace BancoCarrefour.MigrationRunner;

/// <summary>
/// Fronteira de persistência independente (ADR-0002) - cada uma tem seu
/// próprio banco, seu próprio DbContext/conjunto de migrations, e seu
/// próprio lock de migração (nunca compartilhado entre as duas).
/// </summary>
public enum MigrationBoundary
{
    Ledger,
    Consolidation,
}

public static class MigrationBoundaryExtensions
{
    public static MigrationBoundary Parse(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "ledger" => MigrationBoundary.Ledger,
            "consolidation" => MigrationBoundary.Consolidation,
            _ => throw new ArgumentException($"--boundary deve ser 'Ledger' ou 'Consolidation' - valor recebido: '{value}'."),
        };
    }
}

namespace BancoCarrefour.Contracts.Migrations;

/// <summary>
/// Metadata real e verificável em tempo de execução/reflexão da fase de uma
/// migration EF Core - nunca inferida do nome do arquivo ou de convenções
/// textuais. Toda classe <c>Migration</c> real deste repositório (Ledger e
/// Consolidation) deve carregar exatamente um destes atributos; a governança
/// de migração (scripts/ci/validate-migration-governance.sh e o teste
/// espelho em Architecture.Tests) lê este atributo via reflexão, nunca via
/// regex sobre o nome da classe.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MigrationPhaseAttribute(MigrationPhase phase) : Attribute
{
    public MigrationPhase Phase { get; } = phase;
}

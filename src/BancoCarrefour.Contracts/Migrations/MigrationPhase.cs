namespace BancoCarrefour.Contracts.Migrations;

/// <summary>
/// Classificação operacional de uma migration EF Core (ver ADR-0015 e a
/// governança de migração expand-and-contract). Deployment normal só
/// aplica automaticamente migrations <see cref="Expand"/>; <see cref="Backfill"/>
/// e <see cref="Contract"/> exigem um procedimento separado e explicitamente
/// aprovado, nunca disparado pelos workflows de deploy/promoção.
/// </summary>
public enum MigrationPhase
{
    /// <summary>
    /// Mudança aditiva e retrocompatível (nova coluna nullable/com default,
    /// nova tabela, novo índice) - segura para aplicar antes do deploy da
    /// aplicação, mesmo com a revisão anterior ainda em execução durante um
    /// canary/rolling.
    /// </summary>
    Expand,

    /// <summary>
    /// Preenchimento de dados para linhas existentes após um EXPAND - fora
    /// do caminho crítico do deploy, exige lotes limitados, observabilidade,
    /// capacidade de reinício e timeout próprios (ver BackfillRunner).
    /// </summary>
    Backfill,

    /// <summary>
    /// Remove ou restringe algo que o EXPAND tornou obsoleto (DropColumn,
    /// DropTable, RenameColumn/Table, ou AlterColumn tornando uma coluna
    /// NOT NULL) - só pode ser aplicada quando nenhuma revisão anterior
    /// incompatível ainda pode estar em execução, via o comando "contract"
    /// separado e explicitamente aprovado do MigrationRunner.
    /// </summary>
    Contract,
}

using Microsoft.EntityFrameworkCore;

namespace BancoCarrefour.MigrationRunner;

public sealed record BackfillBatchResult(int RowsProcessed, string? NextCheckpoint);

/// <summary>
/// Contrato de uma tarefa de BACKFILL - preenchimento de dados em lotes
/// limitados, fora do caminho crítico do deploy, sempre depois de um
/// EXPAND já aplicado. Nenhuma tarefa concreta está registrada neste
/// repositório hoje (nenhum backfill real é necessário ainda) - esta
/// interface e o runner abaixo materializam a capacidade em si, para que
/// ela nunca precise ser inventada sob pressão quando a primeira
/// necessidade real surgir.
/// </summary>
public interface IBackfillTask
{
    string Name { get; }

    MigrationBoundary Boundary { get; }

    /// <summary>
    /// Processa um único lote limitado a <paramref name="batchSize"/> linhas
    /// e retorna quantas linhas foram efetivamente processadas (0 = tarefa
    /// concluída) e um checkpoint opaco para retomar de onde parou.
    /// </summary>
    Task<BackfillBatchResult> ProcessBatchAsync(DbContext context, string? checkpoint, int batchSize, CancellationToken cancellationToken);
}

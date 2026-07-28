using BancoCarrefour.MigrationRunner;

// Executável one-off dedicado (nunca um dos 4 workloads de negócio, nunca
// um ECS Service de longa duração). Nunca chamado por Ledger.Api,
// Consolidation.Api, Ledger.OutboxPublisher ou Consolidation.Worker no
// próprio startup (esses nunca chamam Database.Migrate()/MigrateAsync()).
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

CliOptions options;
try
{
    options = CliOptions.Parse(args);
}
catch (Exception ex)
{
    StructuredLog.Emit("migration.usage_error", new Dictionary<string, object?>
    {
        // Nunca ex.Message diretamente - um argumento de CLI mal formado
        // poderia, em tese, ecoar um valor sensível informado por engano
        // (ex.: --secret-name colado no lugar errado). O tipo da exceção
        // já basta para localizar a causa no código.
        ["error_type"] = ex.GetType().Name,
    });
    return ExitCode.UsageOrConfigurationError;
}

try
{
    return options.Command switch
    {
        "migrate" => await MigrateCommand.RunAsync(options, cts.Token),
        "contract" => await ContractCommand.RunAsync(options, cts.Token),
        "backfill" => await BackfillCommand.RunAsync(options, cts.Token),
        _ => ExitCode.UsageOrConfigurationError,
    };
}
catch (Exception ex)
{
    StructuredLog.Emit("migration.unhandled_error", new Dictionary<string, object?>
    {
        ["command"] = options.Command,
        ["boundary"] = options.Boundary?.ToString(),
        ["release_id"] = options.ReleaseId,
        // Nunca ex.Message - uma exceção não tratada neste ponto pode vir
        // de qualquer camada, inclusive do Npgsql (cuja mensagem de erro
        // de conexão pode conter host/porta/usuário) - o tipo da exceção
        // já basta para localizar a causa via os logs completos do
        // CloudWatch (nunca truncados, apenas este evento resumido).
        ["error_type"] = ex.GetType().Name,
    });
    return ExitCode.MigrationFailed;
}

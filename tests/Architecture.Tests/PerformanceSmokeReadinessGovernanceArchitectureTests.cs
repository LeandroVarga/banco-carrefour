using Xunit;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Regressão do defeito real encontrado na execução hospedada do
/// Performance smoke gate: a mensagem genérica "consolidation-api não
/// ficou pronto a tempo" não dizia qual camada (Consolidation.Api direto,
/// edge-proxy, banco) estava falhando, nem deixava nenhum log de
/// container disponível. Protege o diagnóstico permanente adicionado em
/// <c>scripts/ci/run-performance-smoke.sh</c> e o contrato de prontidão
/// (banco-backed, orçamento de tempo limitado, load test só após
/// prontidão) sem duplicar a lógica de implementação.
/// </summary>
public sealed class PerformanceSmokeReadinessGovernanceArchitectureTests
{
    [Fact]
    public void Smoke_de_desempenho_deve_aguardar_health_ready_da_consolidation_via_edge()
    {
        var content = ReadRepositoryFile("scripts/ci/run-performance-smoke.sh");

        Assert.Contains("https://localhost:8443/consolidation/health/ready", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Consolidation_Api_deve_manter_a_checagem_de_prontidao_com_backing_real_de_banco()
    {
        // Nunca substituída por /health/live nem removida - a checagem
        // "consolidation-postgres" com tag "ready" é o que garante que
        // /health/ready reflita a conectividade real com o Postgres, não
        // apenas o processo estar de pé.
        var content = ReadRepositoryFile("src/Consolidation/Consolidation.Api/Program.cs");

        Assert.Contains("AddCheck<ConsolidationDatabaseHealthCheck>(\"consolidation-postgres\", tags: [\"ready\"])", content, StringComparison.Ordinal);
        Assert.Contains("MapHealthChecks(\"/health/ready\"", content, StringComparison.Ordinal);
        Assert.Contains("MapHealthChecks(\"/health/live\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Espera_de_prontidao_deve_ter_orcamento_de_tempo_limitado_e_explicito()
    {
        var content = ReadRepositoryFile("scripts/ci/run-performance-smoke.sh");

        // Loop limitado (nunca "while true"/retry irrestrito) com sleep
        // fixo - o orçamento total tem que continuar explícito e
        // auditável no próprio script, não escondido atrás de uma
        // biblioteca de retry.
        Assert.Contains("while [ \"$i\" -lt 60 ]", content, StringComparison.Ordinal);
        Assert.Contains("sleep 2", content, StringComparison.Ordinal);
        Assert.DoesNotContain("while true", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostico_de_prontidao_deve_rodar_antes_do_fail_em_caso_de_falha()
    {
        var content = ReadRepositoryFile("scripts/ci/run-performance-smoke.sh");

        var diagnosticFunctionIndex = content.IndexOf("diagnose_readiness_failure()", StringComparison.Ordinal);
        var diagnosticCallIndex = content.IndexOf("diagnose_readiness_failure\n", StringComparison.Ordinal);
        var failCallIndex = content.IndexOf("fail \"consolidation-api nao ficou pronto a tempo", StringComparison.Ordinal);

        Assert.True(diagnosticFunctionIndex >= 0, "run-performance-smoke.sh deve definir uma função de diagnóstico de prontidão.");
        Assert.True(diagnosticCallIndex >= 0, "a função de diagnóstico deve ser chamada explicitamente antes da falha.");
        Assert.True(failCallIndex >= 0, "a mensagem de falha de prontidão deve continuar presente.");
        Assert.True(diagnosticCallIndex < failCallIndex, "o diagnóstico de prontidão deve rodar ANTES de 'fail' encerrar o processo - o cleanup só roda depois, via o trap de EXIT.");
    }

    [Fact]
    public void Diagnostico_de_prontidao_deve_distinguir_consolidation_api_direto_do_edge_proxy()
    {
        var content = ReadRepositoryFile("scripts/ci/run-performance-smoke.sh");

        var directIndex = content.IndexOf("http://consolidation-api:8080/health/ready", StringComparison.Ordinal);
        var edgeIndex = content.IndexOf("https://localhost:8443/consolidation/health/ready", StringComparison.Ordinal);

        Assert.True(directIndex >= 0, "o diagnóstico deve verificar Consolidation.Api diretamente, na rede interna do Compose.");
        Assert.True(edgeIndex >= 0, "o diagnóstico deve verificar a borda (edge-proxy) separadamente.");
        Assert.Contains("Consolidation.Api direto", content, StringComparison.Ordinal);
        Assert.Contains("edge-proxy (borda TLS", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostico_de_prontidao_nunca_deve_carregar_ou_imprimir_arquivos_de_segredo()
    {
        var content = ReadRepositoryFile("scripts/ci/run-performance-smoke.sh");

        var diagnosticStart = content.IndexOf("diagnose_readiness_failure()", StringComparison.Ordinal);
        var diagnosticEnd = content.IndexOf("\n}\n", diagnosticStart, StringComparison.Ordinal);
        Assert.True(diagnosticStart >= 0 && diagnosticEnd > diagnosticStart);
        var diagnosticBody = content.Substring(diagnosticStart, diagnosticEnd - diagnosticStart);

        Assert.DoesNotContain(".env.security", diagnosticBody, StringComparison.Ordinal);
        Assert.DoesNotContain("MERCHANT_A_TEST_CLIENT_SECRET", diagnosticBody, StringComparison.Ordinal);
        Assert.DoesNotContain("cat .env", diagnosticBody, StringComparison.Ordinal);
        Assert.Contains("sanitize_log", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostico_de_prontidao_deve_capturar_estado_dos_containers_e_logs_sanitizados()
    {
        var content = ReadRepositoryFile("scripts/ci/run-performance-smoke.sh");

        Assert.Contains("docker compose ps -a", content, StringComparison.Ordinal);
        Assert.Contains("docker compose logs --no-color --tail=40", content, StringComparison.Ordinal);
        Assert.Contains("consolidation-api consolidation-postgres consolidation-migrations consolidation-db-grants-bootstrap secret-value-bootstrap terraform-provisioner edge-proxy", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Smoke_de_desempenho_so_deve_iniciar_o_load_test_depois_da_prontidao_confirmada()
    {
        var content = ReadRepositoryFile("scripts/ci/run-performance-smoke.sh");

        var readyGrantedIndex = content.IndexOf("log \"   Pronto.\"", StringComparison.Ordinal);
        var loadTestIndex = content.IndexOf("dotnet run --project tests/Consolidation.LoadTests", StringComparison.Ordinal);

        Assert.True(readyGrantedIndex >= 0 && loadTestIndex >= 0);
        Assert.True(readyGrantedIndex < loadTestIndex, "o smoke de 50 RPS nunca pode iniciar antes da prontidão real ser confirmada.");
    }

    [Fact]
    public void Criterios_de_50_RPS_e_5_por_cento_de_falha_devem_permanecer_inalterados()
    {
        var content = ReadRepositoryFile("scripts/ci/run-performance-smoke.sh");

        Assert.Contains("LOADTEST_RPS:-50", content, StringComparison.Ordinal);
        Assert.Contains("LOADTEST_MAX_FAILURE_RATE:-0.05", content, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return File.ReadAllText(Path.Combine(RepositoryRoot, normalized));
    }

    private static string RepositoryRoot { get; } = LocateRepositoryRoot();

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

using System.Diagnostics;
using Xunit;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Regressão do defeito real encontrado na execução hospedada do
/// Performance smoke gate: <c>infra/edge-proxy/95-stage-tls-key.sh</c> é
/// montado somente-leitura em <c>/docker-entrypoint.d/</c>, cujo mecanismo
/// oficial do nginx só executa scripts com o bit executável - o modo
/// gravado no índice do Git (não a ACL do filesystem Windows, que nunca é
/// autoritativa para um checkout Linux) estava 100644, então um checkout
/// Linux limpo recebia o script sem permissão de execução, o entrypoint
/// oficial o ignorava ("not executable"), a chave TLS nunca era copiada
/// para /etc/nginx/server.key, e o nginx falhava ao iniciar.
/// </summary>
public sealed class EdgeProxyTlsEntrypointGovernanceArchitectureTests
{
    private const string ScriptRelativePath = "infra/edge-proxy/95-stage-tls-key.sh";

    [Fact]
    public void Script_de_stage_da_chave_TLS_deve_estar_rastreado_com_modo_executavel_100755()
    {
        var mode = GetGitIndexMode(ScriptRelativePath);

        Assert.True(
            mode is not null,
            $"'{ScriptRelativePath}' precisa estar rastreado pelo Git.");
        Assert.Equal(
            "100755",
            mode);
    }

    [Fact]
    public void Compose_deve_montar_o_script_de_stage_somente_leitura_no_caminho_oficial_do_entrypoint()
    {
        var content = ReadRepositoryFile("docker-compose.yml");

        Assert.Contains(
            "./infra/edge-proxy/95-stage-tls-key.sh:/docker-entrypoint.d/95-stage-tls-key.sh:ro",
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Script_de_stage_deve_continuar_copiando_a_chave_privada_para_etc_nginx_server_key()
    {
        var composeContent = ReadRepositoryFile("docker-compose.yml");
        var scriptContent = ReadRepositoryFile(ScriptRelativePath);

        // O destino real vem de SSL_CERT_KEY_FILE (definido no compose) -
        // nunca hardcoded como caminho fixo dentro do script, mas o
        // compose tem que continuar apontando para /etc/nginx/server.key,
        // que é onde o template do nginx (abaixo) espera encontrar a chave.
        Assert.Contains("SSL_CERT_KEY_FILE: /etc/nginx/server.key", composeContent, StringComparison.Ordinal);
        Assert.Contains("DEST=\"${SSL_CERT_KEY_FILE:-", scriptContent, StringComparison.Ordinal);
        Assert.Contains("cp \"$SRC\" \"$DEST\"", scriptContent, StringComparison.Ordinal);
        Assert.Contains("chmod 600 \"$DEST\"", scriptContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Template_nginx_deve_continuar_referenciando_SSL_CERT_KEY_FILE()
    {
        var content = ReadRepositoryFile("infra/edge-proxy/default.conf.template");

        Assert.Contains("ssl_certificate_key ${SSL_CERT_KEY_FILE};", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Nenhum_teste_ou_workflow_deve_contornar_a_inicializacao_real_de_TLS_do_edge_proxy()
    {
        var fixtureContent = ReadRepositoryFile("tests/Security.IntegrationTests/Edge/AuthenticatedEdgeFlowFixture.cs");
        var performanceScriptContent = ReadRepositoryFile("scripts/ci/run-performance-smoke.sh");

        // Nunca deve existir um caminho que suba o edge-proxy sem TLS
        // (ex.: apontando para o serviço "consolidation-api"/"ledger-api"
        // diretamente por HTTP simples como se fosse a borda) nem pular a
        // etapa de espera por prontidão via HTTPS.
        Assert.DoesNotContain("http://localhost:8443", fixtureContent, StringComparison.Ordinal);
        Assert.Contains("https://localhost:8443", performanceScriptContent, StringComparison.Ordinal);
    }

    private static string? GetGitIndexMode(string relativePath)
    {
        var startInfo = new ProcessStartInfo("git", $"ls-files --stage {relativePath}")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o processo 'git ls-files --stage'.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'git ls-files --stage' falhou com código de saída {process.ExitCode}. Erro: {error}");
        }

        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (line is null)
        {
            return null;
        }

        // Formato: "<mode> <sha> <stage>\t<path>"
        return line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
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

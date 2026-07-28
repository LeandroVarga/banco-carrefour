using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Regras de regressão do bloco Secrets Manager/SSM/KMS/IAM (ver
/// ADR-0009): nenhum valor de secret entra no Terraform ou é versionado, e o
/// fallback local (legado, com credencial embutida) removido dos
/// composition roots não pode voltar ao código de produção.
/// </summary>
public sealed class SecretsGovernanceArchitectureTests
{
    [Fact]
    public void Terraform_nao_deve_conter_secret_version_com_valor_ou_ssm_secure_string()
    {
        // Checagem estrutural (declaração real de recurso/atributo), não uma
        // busca genérica por palavra solta — comentários explicativos que
        // apenas MENCIONAM esses termos (ex.: "SecureString não é usado
        // aqui") não devem contar como violação.
        var terraformSource = ReadAllFiles(Path.Combine("infra", "terraform"), "*.tf");

        Assert.False(
            Regex.IsMatch(terraformSource, "resource\\s+\"aws_secretsmanager_secret_version\""),
            "Encontrada declaração de resource \"aws_secretsmanager_secret_version\" - valores de secret não podem ser geridos pelo Terraform (ADR-0009).");

        Assert.False(
            Regex.IsMatch(terraformSource, "type\\s*=\\s*\"SecureString\""),
            "Encontrado parâmetro SSM do tipo SecureString - uso restrito a configuração não sensível (ADR-0009).");

        Assert.False(
            Regex.IsMatch(terraformSource, "secret_string\\s*="),
            "Encontrado argumento 'secret_string =' em um recurso Terraform - valores de secret nunca podem ser atribuídos via Terraform (ADR-0009).");
    }

    [Fact]
    public void Terraform_state_plan_e_lockfile_de_execucao_nao_devem_estar_rastreados()
    {
        var trackedPaths = ListGitTrackedPaths();

        Assert.DoesNotContain(trackedPaths, path => path.Contains(".tfstate", StringComparison.Ordinal));
        Assert.DoesNotContain(trackedPaths, path => path.Contains(".tfplan", StringComparison.Ordinal));
        Assert.DoesNotContain(trackedPaths, path => path.Replace('\\', '/').Contains("/.terraform/", StringComparison.Ordinal));
    }

    [Fact]
    public void Env_e_segredos_locais_nao_devem_estar_rastreados()
    {
        var trackedPaths = ListGitTrackedPaths().Select(path => path.Replace('\\', '/')).ToArray();

        Assert.DoesNotContain(trackedPaths, path => path == ".env" || path.EndsWith("/.env", StringComparison.Ordinal));
        Assert.DoesNotContain(trackedPaths, path => path.Contains(".env.security", StringComparison.Ordinal));
        Assert.DoesNotContain(trackedPaths, path => path.StartsWith(".local/security/", StringComparison.Ordinal));
    }

    [Fact]
    public void Chaves_privadas_nao_devem_estar_rastreadas()
    {
        var trackedPaths = ListGitTrackedPaths();

        Assert.DoesNotContain(trackedPaths, path => path.EndsWith(".key", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(trackedPaths, path => path.EndsWith(".pem", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Secret_value_bootstrap_nao_deve_usar_set_dash_x_nem_imprimir_a_variavel_de_senha()
    {
        var scriptPath = Path.Combine(RepositoryRoot, "scripts", "security", "secret-value-bootstrap-impl.sh");
        var script = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("set -x", script, StringComparison.Ordinal);
        Assert.DoesNotContain("set -o xtrace", script, StringComparison.Ordinal);
        Assert.DoesNotContain("echo \"$password\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("echo $password", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Codigo_de_producao_nao_deve_conter_o_fallback_legado_de_credencial_hardcoded()
    {
        // Exclui *DbContextFactory.cs: IDesignTimeDbContextFactory é usado
        // somente pela ferramenta "dotnet ef" em tempo de design (nunca em
        // runtime/Production) e precisa de uma connection string estática
        // própria para introspecção do modelo - padrão aceito do EF Core,
        // não um fallback de segurança.
        var productionSource = ReadAllFiles("src", "*.cs", excludeFileNameSuffix: "DbContextFactory.cs");

        // Removido em favor do Secrets Manager (ADR-0009) - nunca deve voltar
        // como fallback silencioso de Production.
        Assert.DoesNotContain("Password=ledger;", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=consolidation;", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Username=ledger;Password=ledger", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Username=consolidation;Password=consolidation", productionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretsManagerOptions_deve_ter_Enabled_ligado_por_padrao()
    {
        // O bypass (Enabled = false) precisa ser explícito e restrito a
        // ambiente local/testes - o padrão de produção é sempre usar o
        // Secrets Manager.
        var ledgerOptions = new BancoCarrefour.Ledger.Infrastructure.Secrets.SecretsManagerOptions();
        var consolidationOptions = new BancoCarrefour.Consolidation.Infrastructure.Secrets.SecretsManagerOptions();

        Assert.True(ledgerOptions.Enabled);
        Assert.True(consolidationOptions.Enabled);
    }

    [Fact]
    public void SystemsManagerOptions_deve_ter_Enabled_ligado_por_padrao()
    {
        // SSM é a fonte autoritativa de issuer/audience (ADR-0009) - o bypass
        // (Enabled = false) precisa ser explícito e restrito ao ambiente
        // Testing, nunca o padrão.
        var ledgerOptions = new BancoCarrefour.Ledger.Infrastructure.Ssm.SystemsManagerOptions();
        var consolidationOptions = new BancoCarrefour.Consolidation.Infrastructure.Ssm.SystemsManagerOptions();

        Assert.True(ledgerOptions.Enabled);
        Assert.True(consolidationOptions.Enabled);
    }

    [Fact]
    public void Bypass_de_SecretsManager_e_SystemsManager_e_restrito_ao_ambiente_Testing()
    {
        // EnvironmentGuard é internal, mas o campo TestingEnvironmentName é
        // public const - acessível via reflection sem NonPublic. Garante que
        // o nome do ambiente permitido não diverge silenciosamente entre
        // Ledger e Consolidation (ADR-0009/ADR-0009).
        var ledgerGuardType = typeof(BancoCarrefour.Ledger.Infrastructure.Secrets.SecretsManagerOptions).Assembly
            .GetType("BancoCarrefour.Ledger.Infrastructure.EnvironmentGuard", throwOnError: true)!;
        var consolidationGuardType = typeof(BancoCarrefour.Consolidation.Infrastructure.Secrets.SecretsManagerOptions).Assembly
            .GetType("BancoCarrefour.Consolidation.Infrastructure.EnvironmentGuard", throwOnError: true)!;

        var ledgerConstant = (string)ledgerGuardType.GetField("TestingEnvironmentName")!.GetValue(null)!;
        var consolidationConstant = (string)consolidationGuardType.GetField("TestingEnvironmentName")!.GetValue(null)!;

        Assert.Equal("Testing", ledgerConstant);
        Assert.Equal("Testing", consolidationConstant);
    }

    [Fact]
    public void Publisher_e_Worker_nao_devem_referenciar_configuracao_OIDC_via_SSM()
    {
        // Ledger.OutboxPublisher e Consolidation.Worker não autenticam
        // requisições e não devem solicitar issuer/audience (ADR-0009) -
        // checagem textual do próprio Program.cs de cada um.
        var outboxPublisherProgram = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "Ledger", "Ledger.OutboxPublisher", "Program.cs"));
        var consolidationWorkerProgram = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "Consolidation", "Consolidation.Worker", "Program.cs"));

        Assert.DoesNotContain("OidcConfigurationResolver", outboxPublisherProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemsManager", outboxPublisherProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("OidcConfigurationResolver", consolidationWorkerProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemsManager", consolidationWorkerProgram, StringComparison.Ordinal);
    }

    [Fact]
    public void Ledger_Api_e_Consolidation_Api_nao_devem_declarar_Authentication_Authority_Audience_concorrentes_no_compose()
    {
        // Quando SystemsManager está habilitado (padrão), issuer/audience só
        // podem vir do SSM (ADR-0009) - nenhuma variável
        // Authentication__Authority/Audience pode competir no docker-compose
        // real dessas duas APIs.
        var composeText = File.ReadAllText(Path.Combine(RepositoryRoot, "docker-compose.yml"));
        var ledgerApiBlock = ExtractComposeServiceBlock(composeText, "ledger-api");
        var consolidationApiBlock = ExtractComposeServiceBlock(composeText, "consolidation-api");

        // Regex ancorada em início de linha (declaração real de variável de
        // ambiente), não uma busca de substring solta - um comentário que só
        // MENCIONA essas chaves (explicando por que foram removidas) não deve
        // contar como violação.
        var declarationPattern = new Regex(@"^\s*Authentication__(Authority|Audience):", RegexOptions.Multiline);

        Assert.False(declarationPattern.IsMatch(ledgerApiBlock), "ledger-api não pode declarar Authentication__Authority/Audience no docker-compose.yml (ADR-0009).");
        Assert.False(declarationPattern.IsMatch(consolidationApiBlock), "consolidation-api não pode declarar Authentication__Authority/Audience no docker-compose.yml (ADR-0009).");
    }

    [Fact]
    public void Separacao_por_API_audience_parameter_name_distintos_e_publisher_worker_sem_parametros_ssm()
    {
        // Cada API só é configurada (docker-compose real) com o nome do SEU
        // próprio parâmetro de audience - nunca o da outra API. Publisher e
        // Worker não recebem nenhuma variável SystemsManager__ (não
        // consomem OIDC, ver ADR-0009).
        var composeText = File.ReadAllText(Path.Combine(RepositoryRoot, "docker-compose.yml"));
        var ledgerApiBlock = ExtractComposeServiceBlock(composeText, "ledger-api");
        var consolidationApiBlock = ExtractComposeServiceBlock(composeText, "consolidation-api");
        var outboxPublisherBlock = ExtractComposeServiceBlock(composeText, "ledger-outbox-publisher");
        var consolidationWorkerBlock = ExtractComposeServiceBlock(composeText, "consolidation-worker");

        Assert.Contains(
            "SystemsManager__AudienceParameterName: /banco-carrefour/oidc/ledger-audience",
            ledgerApiBlock,
            StringComparison.Ordinal);
        Assert.Contains(
            "SystemsManager__AudienceParameterName: /banco-carrefour/oidc/consolidation-audience",
            consolidationApiBlock,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SystemsManager__AudienceParameterName: /banco-carrefour/oidc/consolidation-audience",
            ledgerApiBlock,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SystemsManager__AudienceParameterName: /banco-carrefour/oidc/ledger-audience",
            consolidationApiBlock,
            StringComparison.Ordinal);

        Assert.DoesNotContain("SystemsManager__", outboxPublisherBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemsManager__", consolidationWorkerBlock, StringComparison.Ordinal);
    }

    private static string ExtractComposeServiceBlock(string composeText, string serviceName)
    {
        var lines = composeText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var startIndex = Array.FindIndex(lines, line => line.StartsWith($"  {serviceName}:", StringComparison.Ordinal));
        Assert.True(startIndex >= 0, $"Serviço '{serviceName}' não encontrado no docker-compose.yml.");

        var blockLines = new List<string> { lines[startIndex] };
        for (var i = startIndex + 1; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], @"^  \S.*:\s*$"))
            {
                break;
            }

            blockLines.Add(lines[i]);
        }

        return string.Join('\n', blockLines);
    }

    private static string ReadAllFiles(string relativeDirectory, string searchPattern, string? excludeFileNameSuffix = null)
    {
        var directory = Path.Combine(RepositoryRoot, relativeDirectory);

        if (!Directory.Exists(directory))
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(Path.GetRelativePath(RepositoryRoot, path)))
                .Where(path => excludeFileNameSuffix is null || !Path.GetFileName(path).EndsWith(excludeFileNameSuffix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static bool IsBuildOutput(string relativePath)
    {
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("bin", StringComparer.Ordinal) || segments.Contains("obj", StringComparer.Ordinal);
    }

    private static IReadOnlyCollection<string> ListGitTrackedPaths()
    {
        var startInfo = new ProcessStartInfo("git", "ls-files")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o processo 'git ls-files'.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'git ls-files' falhou com código de saída {process.ExitCode}. Erro: {error}");
        }

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim('\r'))
            .ToArray();
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

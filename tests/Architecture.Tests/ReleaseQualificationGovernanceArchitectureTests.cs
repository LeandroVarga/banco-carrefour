using System.Linq;
using YamlDotNet.RepresentationModel;
using Xunit;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Governança da única capacidade de release-qualification (ver
/// ADR-0013): garante, por parsing real do Compose/YAML, que o runtime de qualificação nunca
/// reconstrói/publica imagens, que nenhum workflow referencia um nome de
/// GitHub Environment fora do conjunto aprovado para os ambientes AWS
/// reais, e que a qualificação sempre encerra a stack.
/// </summary>
public sealed class ReleaseQualificationGovernanceArchitectureTests
{
    [Fact]
    public void Compose_de_release_qualification_nao_deve_ter_diretiva_build_em_nenhum_servico()
    {
        var yaml = new YamlStream();
        using var reader = new StreamReader(Path.Combine(RepositoryRoot, "deploy", "compose", "docker-compose.release-qualification.yml"));
        yaml.Load(reader);

        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var services = (YamlMappingNode)root.Children[new YamlScalarNode("services")];

        foreach (var serviceEntry in services.Children)
        {
            var service = (YamlMappingNode)serviceEntry.Value;
            Assert.False(
                service.Children.ContainsKey(new YamlScalarNode("build")),
                $"serviço '{((YamlScalarNode)serviceEntry.Key).Value}' não deveria ter 'build:' no compose de release-qualification - imagem sempre a build-once local, nunca reconstruída.");
        }
    }

    [Fact]
    public void Compose_de_release_qualification_deve_exigir_os_4_image_refs_via_variavel_obrigatoria()
    {
        var content = ReadRepositoryFile("deploy/compose/docker-compose.release-qualification.yml");

        Assert.Contains("${LEDGER_API_IMAGE_REF:?", content, StringComparison.Ordinal);
        Assert.Contains("${LEDGER_OUTBOX_PUBLISHER_IMAGE_REF:?", content, StringComparison.Ordinal);
        Assert.Contains("${CONSOLIDATION_API_IMAGE_REF:?", content, StringComparison.Ordinal);
        Assert.Contains("${CONSOLIDATION_WORKER_IMAGE_REF:?", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_de_release_qualification_nunca_deve_referenciar_registry_remoto_nos_4_componentes()
    {
        // As 4 imagens da aplicacao sao referenciadas pela variavel
        // *_IMAGE_REF (resolvida para a tag LOCAL build-once) - nunca um
        // literal "ghcr.io/" ou ".dkr.ecr." hardcoded no proprio compose.
        var content = ReadRepositoryFile("deploy/compose/docker-compose.release-qualification.yml");

        Assert.DoesNotContain("ghcr.io/", content, StringComparison.Ordinal);
        Assert.DoesNotContain(".dkr.ecr.", content, StringComparison.Ordinal);
    }

    private static readonly string[] ApprovedEnvironmentNames =
    [
        "development",
        "staging",
        "production"
    ];

    [Fact]
    public void Nenhum_workflow_deve_referenciar_um_nome_de_GitHub_Environment_fora_do_conjunto_aprovado()
    {
        // Verifica TODOS os jobs com chave "environment:" em TODOS os
        // workflows - o valor (forma curta "environment: x" OU forma longa
        // "environment: { name: x }") precisa ser exatamente um dos 3
        // nomes aprovados, que representam contas/ambientes AWS reais
        // (nunca "development-simulation"/"staging-simulation"/
        // "production-simulation", removidos com o
        // ADR-0013).
        var workflowsDirectory = Path.Combine(RepositoryRoot, ".github", "workflows");
        foreach (var workflowFile in Directory.GetFiles(workflowsDirectory, "*.yml"))
        {
            var yaml = new YamlStream();
            using var reader = new StreamReader(workflowFile);
            yaml.Load(reader);

            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            {
                continue;
            }

            if (!root.Children.TryGetValue(new YamlScalarNode("jobs"), out var jobsNode) || jobsNode is not YamlMappingNode jobs)
            {
                continue;
            }

            foreach (var jobEntry in jobs.Children)
            {
                var job = Assert.IsType<YamlMappingNode>(jobEntry.Value);
                if (!job.Children.TryGetValue(new YamlScalarNode("environment"), out var environmentNode))
                {
                    continue;
                }

                string? environmentName = environmentNode switch
                {
                    YamlScalarNode scalar => scalar.Value,
                    YamlMappingNode mapping when mapping.Children.TryGetValue(new YamlScalarNode("name"), out var nameNode) => ((YamlScalarNode)nameNode).Value,
                    _ => null
                };

                Assert.True(
                    environmentName is not null && ApprovedEnvironmentNames.Contains(environmentName),
                    $"{Path.GetFileName(workflowFile)}, job '{((YamlScalarNode)jobEntry.Key).Value}': nome de GitHub Environment '{environmentName}' fora do conjunto aprovado ({string.Join(", ", ApprovedEnvironmentNames)}).");
            }
        }
    }

    [Fact]
    public void Nenhum_arquivo_do_repositorio_deve_referenciar_nomes_de_ambiente_simulado_abandonados()
    {
        // Nomes de ambiente simulado abandonados nunca devem aparecer em
        // nenhum workflow, script ou documento vivo. Este próprio teste é a
        // única exceção esperada, pois precisa citar os nomes para
        // verificá-los.
        var abandonedNames = new[] { "development-simulation", "staging-simulation", "production-simulation" };
        var allowedFiles = new[]
        {
            Path.Combine("tests", "Architecture.Tests", "ReleaseQualificationGovernanceArchitectureTests.cs"),
        };

        var scannedRoots = new[] { ".github", "deploy", "docs", "scripts", "schemas", "tests" };
        var textExtensions = new[] { ".yml", ".yaml", ".md", ".sh", ".json", ".cs" };

        foreach (var scannedRoot in scannedRoots)
        {
            var rootPath = Path.Combine(RepositoryRoot, scannedRoot);
            if (!Directory.Exists(rootPath))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                if (!textExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(RepositoryRoot, file).Replace('\\', '/');
                if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
                {
                    continue;
                }
                if (allowedFiles.Any(allowed => relative == allowed.Replace('\\', '/')))
                {
                    continue;
                }

                var content = File.ReadAllText(file);

                foreach (var abandoned in abandonedNames)
                {
                    Assert.False(
                        content.Contains(abandoned, StringComparison.Ordinal),
                        $"{relative} referencia o nome de ambiente abandonado '{abandoned}' - ver ADR-0013.");
                }
            }
        }
    }

    [Fact]
    public void Workflow_de_release_qualification_nunca_deve_disparar_em_pull_request()
    {
        var content = ReadRepositoryFile(".github/workflows/release-qualification.yml");
        Assert.DoesNotContain("pull_request", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_de_release_qualification_nunca_deve_publicar_em_nenhum_registry()
    {
        var content = ReadRepositoryFile(".github/workflows/release-qualification.yml");

        Assert.DoesNotContain("docker push", content, StringComparison.Ordinal);
        Assert.DoesNotContain("configure-aws-credentials", content, StringComparison.Ordinal);
        Assert.DoesNotContain("amazon-ecr-login", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_de_release_qualification_deve_sempre_encerrar_a_stack_mesmo_em_falha()
    {
        var content = ReadRepositoryFile("scripts/ci/run-release-qualification.sh");

        Assert.Contains("trap teardown EXIT INT TERM", content, StringComparison.Ordinal);
        // "--profile manual-tools": obrigatorio para que "down -v" tambem
        // remova containers de servicos sob demanda (keycloak-bootstrap,
        // dotnet-sdk) - sem essa flag, "docker compose down" trata
        // servicos por tras de profile como "desabilitados", nunca como
        // residuo/orfao (achado real, confirmado por inspecao direta do
        // Docker apos o teardown).
        Assert.Contains("docker compose -f \"$COMPOSE_FILE\" --profile manual-tools down -v", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_de_release_qualification_nunca_deve_reconstruir_ou_fazer_pull_de_imagens()
    {
        var content = ReadRepositoryFile("scripts/ci/run-release-qualification.sh");

        Assert.DoesNotContain("--build", content, StringComparison.Ordinal);
        Assert.DoesNotContain("docker pull", content, StringComparison.Ordinal);
        Assert.DoesNotContain("docker compose pull", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Gerador_de_credenciais_efemeras_deve_usar_fonte_aleatoria_real_e_nunca_valor_fixo()
    {
        var content = ReadRepositoryFile("scripts/release/generate-ephemeral-environment-secrets.sh");

        Assert.Contains("/dev/urandom", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Deploy_releases_readme_nunca_deve_conter_um_releaseId_commitado()
    {
        var content = ReadRepositoryFile("deploy/releases/README.md");

        // O README documenta a AUSENCIA de manifestos commitados - garante
        // que ninguem adicione um "sha-<hex>" fixo aqui como se fosse uma
        // release real.
        Assert.DoesNotContain("\"releaseId\":", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_images_deve_rodar_release_qualification_antes_de_autenticar_na_AWS()
    {
        // Gate real: release-qualification precisa terminar com sucesso
        // ANTES da configuracao de credenciais AWS/publicacao no ECR - a
        // publicacao nunca deve prosseguir se a qualificacao falhar.
        var content = ReadRepositoryFile(".github/workflows/publish-images.yml");

        var qualificationIndex = content.IndexOf("run: sh scripts/ci/run-release-qualification.sh", StringComparison.Ordinal);
        var configureCredentialsIndex = content.IndexOf("configure-aws-credentials", StringComparison.Ordinal);

        Assert.True(qualificationIndex >= 0 && configureCredentialsIndex >= 0, "publish-images.yml deve conter release-qualification e a configuracao de credenciais AWS.");
        Assert.True(qualificationIndex < configureCredentialsIndex, "release-qualification deve rodar ANTES de autenticar na AWS.");
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

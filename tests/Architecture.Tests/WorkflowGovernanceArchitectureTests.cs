using System.Text.RegularExpressions;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Governança estrutural dos workflows do GitHub Actions:
/// garante, por parsing real do YAML (não por substring frágil), que as
/// regras de segurança da baseline de CI (ADR-0012) continuam valendo -
/// sem depender de execução real em runner hospedado pelo GitHub.
/// </summary>
public sealed class WorkflowGovernanceArchitectureTests
{
    // owner/repo[/subpath...]@<sha40> - o grupo de subpath cobre actions
    // compostas por sub-diretorio (ex.: github/codeql-action/init@<sha>).
    private static readonly Regex PinnedActionReference = new(@"^[^/@]+/[^/@]+(/[^/@]+)*@[0-9a-f]{40}(\s*#.*)?$", RegexOptions.Compiled);
    private static readonly Regex AwsAccessKeyIdPattern = new(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled);
    private static readonly string[] ForbiddenDockerPruneCommands =
    [
        "docker system prune",
        "docker container prune",
        "docker volume prune",
        "docker network prune"
    ];

    private static readonly string[] ForbiddenArtifactPathSegments =
    [
        ".local/security",
        ".env",
        ".pem",
        ".key",
        "terraform.tfstate",
        "tfplan"
    ];

    [Fact]
    public void Workflows_devem_ser_parseados_com_sucesso_como_YAML()
    {
        foreach (var workflowFile in ListWorkflowFiles())
        {
            var yaml = new YamlStream();
            using var reader = new StreamReader(workflowFile);

            var exception = Record.Exception(() => yaml.Load(reader));

            Assert.True(exception is null, $"{RelativePath(workflowFile)} não é um YAML válido: {exception}");
            Assert.NotEmpty(yaml.Documents);
        }
    }

    [Fact]
    public void Workflows_nao_devem_usar_pull_request_target()
    {
        foreach (var workflowFile in ListWorkflowFiles())
        {
            var content = File.ReadAllText(workflowFile);

            Assert.False(
                content.Contains("pull_request_target", StringComparison.Ordinal),
                $"{RelativePath(workflowFile)} não deve usar pull_request_target para build/teste de código não confiável.");
        }
    }

    [Fact]
    public void Workflows_devem_declarar_permissions_minimas_no_nivel_do_workflow()
    {
        foreach (var workflowFile in ListWorkflowFiles())
        {
            var root = LoadRootMapping(workflowFile);

            var permissionsNode = GetChild(root, "permissions");
            Assert.True(permissionsNode is not null, $"{RelativePath(workflowFile)} deve declarar 'permissions' no nível do workflow.");

            var permissionsMapping = Assert.IsType<YamlMappingNode>(permissionsNode);
            var contentsValue = GetScalarValue(permissionsMapping, "contents");
            Assert.Equal("read", contentsValue);

            // Nenhum job deste bloco precisa de id-token (sem OIDC/federação
            // com AWS neste bloco - ver seção "fora deste bloco" ).
            Assert.Null(GetChild(permissionsMapping, "id-token"));
        }
    }

    [Fact]
    public void Workflows_nao_devem_conter_credenciais_estaticas_da_AWS()
    {
        foreach (var workflowFile in ListWorkflowFiles())
        {
            var content = File.ReadAllText(workflowFile);

            Assert.False(
                AwsAccessKeyIdPattern.IsMatch(content),
                $"{RelativePath(workflowFile)} não deve conter um Access Key ID real da AWS (padrão AKIA...).");
        }
    }

    [Fact]
    public void Referencias_de_actions_de_terceiros_devem_ser_por_commit_SHA_imutavel()
    {
        foreach (var workflowFile in ListWorkflowFiles())
        {
            foreach (var usesValue in FindAllUsesValues(workflowFile))
            {
                Assert.True(
                    PinnedActionReference.IsMatch(usesValue),
                    $"{RelativePath(workflowFile)}: 'uses: {usesValue}' deve referenciar um commit SHA de 40 caracteres (não uma tag/branch flutuante como @v4 ou @main).");
            }
        }
    }

    [Fact]
    public void Gate_de_desempenho_deve_manter_alvo_de_50_RPS_e_limite_de_5_por_cento()
    {
        var ciWorkflow = ListWorkflowFiles().Single(x => Path.GetFileName(x) == "ci.yml");
        var root = LoadRootMapping(ciWorkflow);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var performanceJob = Assert.IsType<YamlMappingNode>(GetChild(jobs, "performance-smoke-gate"));
        var env = Assert.IsType<YamlMappingNode>(GetChild(performanceJob, "env"));

        Assert.Equal("50", GetScalarValue(env, "LOADTEST_RPS"));
        Assert.Equal("0.05", GetScalarValue(env, "LOADTEST_MAX_FAILURE_RATE"));
    }

    [Fact]
    public void Concorrencia_nao_deve_cancelar_incondicionalmente_execucoes_de_push_ou_workflow_dispatch()
    {
        // Regressão: "cancel-in-progress: true" incondicional cancelaria a
        // validação de um commit já incorporado a main assim que outro push
        // chegasse - o grupo/cancelamento precisam depender do tipo de
        // evento (PR isolado por número; push/workflow_dispatch isolado por
        // SHA, nunca cancelado por outra execução).
        foreach (var workflowFile in ListWorkflowFiles())
        {
            var root = LoadRootMapping(workflowFile);
            if (GetChild(root, "concurrency") is not YamlMappingNode concurrency)
            {
                continue;
            }

            var cancelInProgress = GetScalarValue(concurrency, "cancel-in-progress");
            Assert.False(
                string.Equals(cancelInProgress, "true", StringComparison.OrdinalIgnoreCase),
                $"{RelativePath(workflowFile)}: 'cancel-in-progress: true' incondicional cancelaria validação de push/workflow_dispatch já incorporados a main.");

            // A exigência de isolar o grupo de concorrência por número do PR só
            // se aplica a workflows que de fato disparam em pull_request - um
            // workflow que nunca roda nesse evento (ex.: publish-images.yml,
            // restrito a workflow_dispatch manual) não
            // tem esse número disponível e não deveria fingir isolar por ele.
            if (HasPullRequestTrigger(root))
            {
                var group = GetScalarValue(concurrency, "group");
                Assert.True(
                    group is not null && group.Contains("github.event.pull_request.number", StringComparison.Ordinal),
                    $"{RelativePath(workflowFile)}: 'concurrency.group' deve isolar execuções de pull_request por número do PR.");
            }
        }
    }

    private static bool HasPullRequestTrigger(YamlMappingNode root)
    {
        var onNode = GetChild(root, "on");
        return onNode switch
        {
            YamlMappingNode onMapping => GetChild(onMapping, "pull_request") is not null,
            YamlScalarNode onScalar => onScalar.Value == "pull_request",
            YamlSequenceNode onSequence => onSequence.Any(n => n is YamlScalarNode s && s.Value == "pull_request"),
            _ => false,
        };
    }

    [Fact]
    public void Dependabot_deve_cobrir_nuget_actions_e_docker()
    {
        var dependabotFile = Path.Combine(RepositoryRoot, ".github", "dependabot.yml");
        Assert.True(File.Exists(dependabotFile), "docs/.github/dependabot.yml deve existir (governança de dependências).");

        var root = LoadRootMapping(dependabotFile);
        var updates = Assert.IsType<YamlSequenceNode>(GetChild(root, "updates"));

        var ecosystems = updates
            .Select(node => GetScalarValue((YamlMappingNode)node, "package-ecosystem"))
            .Where(x => x is not null)
            .ToHashSet();

        foreach (var expected in new[] { "nuget", "github-actions", "docker" })
        {
            Assert.True(ecosystems.Contains(expected), $"dependabot.yml deve cobrir o ecossistema '{expected}'.");
        }
    }

    [Fact]
    public void Dependabot_deve_preservar_as_quatro_entradas_docker_por_Dockerfile()
    {
        var updates = LoadDependabotUpdates();

        var dockerDirectories = updates
            .Where(n => GetScalarValue(n, "package-ecosystem") == "docker")
            .Select(n => GetScalarValue(n, "directory"))
            .Where(x => x is not null)
            .ToHashSet();

        foreach (var expected in new[]
        {
            "/src/Ledger/Ledger.Api",
            "/src/Ledger/Ledger.OutboxPublisher",
            "/src/Consolidation/Consolidation.Api",
            "/src/Consolidation/Consolidation.Worker",
        })
        {
            Assert.Contains(expected, dockerDirectories);
        }
    }

    [Fact]
    public void Dependabot_terraform_deve_usar_directories_plural_e_nao_o_escalar_obsoleto()
    {
        // "directory: /infra/terraform" nunca encontrou nenhum arquivo .tf
        // (todas as raízes Terraform reais ficam em subdiretórios de
        // environments/modules - ver teste de cobertura abaixo) - corrigido
        // para "directories" (plural, com glob de um segmento) apontando
        // para as duas famílias de raiz real.
        var updates = LoadDependabotUpdates();
        var terraformEntry = updates.SingleOrDefault(n => GetScalarValue(n, "package-ecosystem") == "terraform");

        Assert.True(terraformEntry is not null, "dependabot.yml deve conter uma entrada 'package-ecosystem: terraform'.");
        Assert.Null(GetChild(terraformEntry!, "directory"));

        var directoriesNode = Assert.IsType<YamlSequenceNode>(GetChild(terraformEntry!, "directories"));
        var directories = directoriesNode.Select(n => ((YamlScalarNode)n).Value).ToArray();

        Assert.Contains("/infra/terraform/environments/*", directories);
        Assert.Contains("/infra/terraform/modules/*", directories);
    }

    [Fact]
    public void Todas_as_raizes_Terraform_rastreadas_com_tf_devem_estar_cobertas_pelo_Dependabot()
    {
        var updates = LoadDependabotUpdates();
        var terraformEntry = updates.Single(n => GetScalarValue(n, "package-ecosystem") == "terraform");
        var directoriesNode = Assert.IsType<YamlSequenceNode>(GetChild(terraformEntry, "directories"));
        var patterns = directoriesNode.Select(n => ((YamlScalarNode)n).Value!.TrimStart('/')).ToArray();

        var tracked = ListTrackedFiles();
        var terraformRoots = tracked
            .Where(p => p.StartsWith("infra/terraform/", StringComparison.Ordinal) && p.EndsWith(".tf", StringComparison.Ordinal))
            .Select(p => Path.GetDirectoryName(p)!.Replace('\\', '/'))
            .Distinct()
            .ToArray();

        Assert.NotEmpty(terraformRoots);

        foreach (var root in terraformRoots)
        {
            var covered = patterns.Any(pattern => DirectoryMatchesSingleSegmentGlob(root, pattern));
            Assert.True(covered, $"Raiz Terraform rastreada '{root}' (contém .tf) não é coberta por nenhum path do Dependabot ({string.Join(", ", patterns)}).");
        }

        // Confirma a premissa da correção: nenhum .tf rastreado diretamente
        // em infra/terraform (a raiz da entrada escalar obsoleta).
        Assert.DoesNotContain(tracked, p => p.StartsWith("infra/terraform/", StringComparison.Ordinal)
            && p.EndsWith(".tf", StringComparison.Ordinal)
            && Path.GetDirectoryName(p)!.Replace('\\', '/') == "infra/terraform");
    }

    private static bool DirectoryMatchesSingleSegmentGlob(string trackedDirectory, string globPattern)
    {
        // globPattern ex.: "infra/terraform/environments/*" - "*" cobre
        // exatamente um segmento de path (o nome do ambiente/módulo),
        // nunca recursivo - mesma semântica de "directories" do Dependabot.
        if (!globPattern.EndsWith("/*", StringComparison.Ordinal))
        {
            return false;
        }

        var prefix = globPattern[..^1];
        if (!trackedDirectory.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = trackedDirectory[prefix.Length..];
        return remainder.Length > 0 && !remainder.Contains('/');
    }

    private static IReadOnlyList<YamlMappingNode> LoadDependabotUpdates()
    {
        var dependabotFile = Path.Combine(RepositoryRoot, ".github", "dependabot.yml");
        var root = LoadRootMapping(dependabotFile);
        var updates = Assert.IsType<YamlSequenceNode>(GetChild(root, "updates"));

        return updates.Select(n => (YamlMappingNode)n).ToArray();
    }

    [Fact]
    public void Workflows_nao_devem_conter_instalacao_via_curl_ou_wget_pipe_shell()
    {
        foreach (var workflowFile in ListWorkflowFiles())
        {
            var content = File.ReadAllText(workflowFile);

            Assert.False(
                Regex.IsMatch(content, @"(curl|wget)[^\n]*\|\s*(sh|bash)\b"),
                $"{RelativePath(workflowFile)} não deve instalar nada via 'curl|sh' ou 'wget|sh'.");
        }
    }

    [Fact]
    public void Workflows_nao_devem_publicar_ou_fazer_push_de_imagens_neste_bloco()
    {
        foreach (var workflowFile in ListWorkflowFiles())
        {
            var content = File.ReadAllText(workflowFile);

            Assert.False(content.Contains("docker push", StringComparison.Ordinal), $"{RelativePath(workflowFile)} não deve publicar imagens neste bloco.");
            Assert.False(content.Contains("docker/build-push-action", StringComparison.Ordinal), $"{RelativePath(workflowFile)} não deve usar build-push-action neste bloco.");
        }
    }

    [Fact]
    public void Workflows_nao_devem_solicitar_permissoes_de_publicacao_ou_atestacao_fora_do_workflow_de_publicacao()
    {
        // publish-images.yml (ADR-0013 - e atestação
        // real) é a ÚNICA exceção justificada a
        // "attestations: write"/"id-token: write" - publica de fato as 4
        // imagens no Amazon ECR (único registry oficial - GHCR nunca foi
        // produção real, ver ADR-0013) e gera atestações reais de
        // proveniência/SBOM associadas ao digest ECR remoto, com as
        // permissões escopadas ao job (não ao workflow inteiro) e
        // documentadas. "packages: write" nunca é necessário em nenhum
        // workflow (era específico do GHCR, caminho abandonado). Nenhum
        // outro workflow tem motivo para essas permissões.
        var workflowsAllowedElevatedPermissions = new[] { "publish-images.yml" };

        foreach (var workflowFile in ListWorkflowFiles())
        {
            var root = LoadRootMapping(workflowFile);
            var content = File.ReadAllText(workflowFile);
            var fileName = Path.GetFileName(workflowFile);

            Assert.False(content.Contains("packages: write", StringComparison.Ordinal), $"{RelativePath(workflowFile)} não deve solicitar 'packages: write' (específico do GHCR, caminho abandonado - ver ADR-0013).");

            if (!workflowsAllowedElevatedPermissions.Contains(fileName))
            {
                Assert.False(content.Contains("attestations: write", StringComparison.Ordinal), $"{RelativePath(workflowFile)} não deve solicitar 'attestations: write'.");
            }

            if (GetChild(root, "permissions") is YamlMappingNode rootPermissions)
            {
                Assert.Null(GetChild(rootPermissions, "id-token"));
            }
        }
    }

    [Fact]
    public void Scripts_de_scanner_devem_referenciar_imagens_por_digest_imutavel()
    {
        var scriptsDirectory = Path.Combine(RepositoryRoot, "scripts", "ci");
        if (!Directory.Exists(scriptsDirectory))
        {
            return;
        }

        foreach (var scriptFile in Directory.GetFiles(scriptsDirectory, "*.sh"))
        {
            var content = File.ReadAllText(scriptFile);

            foreach (Match match in Regex.Matches(content, @"TRIVY_IMAGE=""([^""]+)"""))
            {
                var reference = match.Groups[1].Value;
                Assert.Matches(@"@sha256:[0-9a-f]{64}$", reference);
            }
        }
    }

    [Fact]
    public void Workflows_nao_devem_conter_comando_amplo_de_docker_prune()
    {
        foreach (var workflowFile in ListWorkflowFiles())
        {
            var content = File.ReadAllText(workflowFile);

            foreach (var forbidden in ForbiddenDockerPruneCommands)
            {
                Assert.False(
                    content.Contains(forbidden, StringComparison.Ordinal),
                    $"{RelativePath(workflowFile)} não deve conter '{forbidden}' (comando amplo, não escopado ao projeto).");
            }
        }
    }

    [Fact]
    public void Workflows_nao_devem_usar_docker_compose_down_com_remocao_de_volumes()
    {
        foreach (var workflowFile in ListWorkflowFiles())
        {
            var content = File.ReadAllText(workflowFile);

            Assert.False(
                Regex.IsMatch(content, @"docker compose down\s+(--volumes|-v)\b"),
                $"{RelativePath(workflowFile)} não deve usar 'docker compose down -v' (o ambiente local do desenvolvedor não deve ser destruído).");
        }
    }

    [Fact]
    public void Artefatos_publicados_nao_devem_incluir_caminhos_de_segredo_conhecidos()
    {
        foreach (var workflowFile in ListWorkflowFiles())
        {
            var root = LoadRootMapping(workflowFile);
            var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));

            foreach (var jobEntry in jobs.Children)
            {
                var job = Assert.IsType<YamlMappingNode>(jobEntry.Value);
                var stepsNode = GetChild(job, "steps");
                if (stepsNode is not YamlSequenceNode steps)
                {
                    continue;
                }

                foreach (var stepNode in steps)
                {
                    var step = Assert.IsType<YamlMappingNode>(stepNode);
                    var withNode = GetChild(step, "with");
                    if (withNode is not YamlMappingNode with)
                    {
                        continue;
                    }

                    var pathNode = GetChild(with, "path");
                    if (pathNode is null)
                    {
                        continue;
                    }

                    var pathText = pathNode is YamlScalarNode scalar ? scalar.Value ?? string.Empty : pathNode.ToString();

                    foreach (var forbiddenSegment in ForbiddenArtifactPathSegments)
                    {
                        Assert.False(
                            pathText.Contains(forbiddenSegment, StringComparison.OrdinalIgnoreCase),
                            $"{RelativePath(workflowFile)}: artifact path '{pathText}' não deve referenciar '{forbiddenSegment}'.");
                    }
                }
            }
        }
    }

    // --- Auditoria (correcao de provenance, CodeQL e
    // politica de excecao) - controles estruturais concretos, sem construir
    // um motor de politica generico. ---

    [Fact]
    public void CodeQL_nao_deve_delegar_o_build_rastreado_para_um_container_irmao()
    {
        // O tracer do CodeQL (variaveis de ambiente injetadas pelo "init")
        // nao e herdado por um processo delegado a "docker compose run" em
        // outro container - a compilacao ali nunca seria observada pela
        // analise (build-mode manual silenciosamente vazio). Corrigido para
        // rodar nativamente no runner via actions/setup-dotnet.
        var supplyChainWorkflow = ListWorkflowFiles().Single(x => Path.GetFileName(x) == "supply-chain.yml");
        var root = LoadRootMapping(supplyChainWorkflow);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var codeqlJob = Assert.IsType<YamlMappingNode>(GetChild(jobs, "codeql"));
        var steps = Assert.IsType<YamlSequenceNode>(GetChild(codeqlJob, "steps"));

        foreach (var stepNode in steps)
        {
            var step = Assert.IsType<YamlMappingNode>(stepNode);
            var run = GetScalarValue(step, "run");
            if (run is null)
            {
                continue;
            }

            Assert.False(
                run.Contains("docker compose run", StringComparison.Ordinal),
                "supply-chain.yml: o job 'codeql' não deve rodar o build rastreado via 'docker compose run' (container-irmão) - o tracer do CodeQL não observaria essa compilação.");
        }

        Assert.Contains(
            FindAllUsesValues(supplyChainWorkflow),
            uses => uses.Contains("actions/setup-dotnet", StringComparison.Ordinal));
    }

    [Fact]
    public void CodeQL_deve_declarar_um_build_mode_explicito()
    {
        var supplyChainWorkflow = ListWorkflowFiles().Single(x => Path.GetFileName(x) == "supply-chain.yml");
        var root = LoadRootMapping(supplyChainWorkflow);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var codeqlJob = Assert.IsType<YamlMappingNode>(GetChild(jobs, "codeql"));
        var steps = Assert.IsType<YamlSequenceNode>(GetChild(codeqlJob, "steps"));

        var initStep = steps
            .Select(s => (YamlMappingNode)s)
            .SingleOrDefault(s => (GetScalarValue(s, "uses") ?? string.Empty).Contains("codeql-action/init", StringComparison.Ordinal));

        Assert.True(initStep is not null, "supply-chain.yml: o job 'codeql' deve conter um passo 'github/codeql-action/init'.");
        var with = Assert.IsType<YamlMappingNode>(GetChild(initStep!, "with"));
        var buildMode = GetScalarValue(with, "build-mode");

        Assert.True(
            buildMode is "manual" or "autobuild" or "none",
            $"supply-chain.yml: 'build-mode' do CodeQL deve ser explicito (manual/autobuild/none), encontrado: '{buildMode}'.");
    }

    [Fact]
    public void Security_gate_deve_executar_o_teste_nativamente_no_runner_e_nao_via_container_irmao()
    {
        // Security.IntegrationTests cria containers Keycloak/PostgreSQL
        // ad-hoc via Testcontainers e acessa a porta publicada via
        // "127.0.0.1" diretamente (IdentityFixture.cs) - isso só é
        // alcançável quando o processo de teste roda no MESMO namespace de
        // rede do host (nativo no runner), não dentro de um container-irmão
        // "dotnet-sdk" (reproduzido e confirmado localmente - ver
        // docs/security/dependencias-e-supply-chain.md).
        var ciWorkflow = ListWorkflowFiles().Single(x => Path.GetFileName(x) == "ci.yml");
        var root = LoadRootMapping(ciWorkflow);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var securityGate = Assert.IsType<YamlMappingNode>(GetChild(jobs, "security-gate"));
        var steps = Assert.IsType<YamlSequenceNode>(GetChild(securityGate, "steps"));

        var testStep = steps
            .Select(s => (YamlMappingNode)s)
            .SingleOrDefault(s => (GetScalarValue(s, "run") ?? string.Empty).Contains("Security.IntegrationTests.csproj", StringComparison.Ordinal));

        Assert.True(testStep is not null, "ci.yml: o job 'security-gate' deve conter um passo que rode Security.IntegrationTests.csproj.");
        var runCommand = GetScalarValue(testStep!, "run") ?? string.Empty;

        Assert.False(
            runCommand.Contains("docker compose run", StringComparison.Ordinal),
            "ci.yml: 'security-gate' não deve rodar Security.IntegrationTests via 'docker compose run' (container-irmão) - use processo nativo no runner.");

        Assert.Contains(
            FindAllUsesValues(ciWorkflow),
            uses => uses.Contains("actions/setup-dotnet", StringComparison.Ordinal));
    }

    [Fact]
    public void Script_de_build_de_imagens_deve_exigir_arvore_de_trabalho_limpa()
    {
        var buildScript = Path.Combine(RepositoryRoot, "scripts", "ci", "build-images-for-supply-chain.sh");
        Assert.True(File.Exists(buildScript), "scripts/ci/build-images-for-supply-chain.sh deve existir.");

        var guardScript = Path.Combine(RepositoryRoot, "scripts", "ci", "require-clean-source-tree.sh");
        Assert.True(File.Exists(guardScript), "scripts/ci/require-clean-source-tree.sh deve existir (guarda de provenance).");

        var content = File.ReadAllText(buildScript);
        Assert.Contains("require-clean-source-tree.sh", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_de_build_de_imagens_deve_usar_a_SHA_completa_como_identificador_de_origem()
    {
        var buildScript = Path.Combine(RepositoryRoot, "scripts", "ci", "build-images-for-supply-chain.sh");
        var content = File.ReadAllText(buildScript);

        Assert.Contains("SOURCE_COMMIT=\"$(git rev-parse HEAD)\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("git rev-parse --short", content, StringComparison.Ordinal);
        Assert.Contains("\"sourceTreeClean\": true", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Validador_de_evidencias_deve_comparar_o_sourceCommit_com_o_HEAD_atual()
    {
        var validateScript = Path.Combine(RepositoryRoot, "scripts", "ci", "validate-supply-chain-artifacts.sh");
        var content = File.ReadAllText(validateScript);

        Assert.Contains("SUPPLY_CHAIN_EXPECTED_HEAD", content, StringComparison.Ordinal);
        Assert.Contains("expected_head", content, StringComparison.Ordinal);
        Assert.Contains("sourceTreeClean", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_de_scan_deve_suportar_os_vereditos_pass_with_exceptions_e_scan_error()
    {
        var scanScript = Path.Combine(RepositoryRoot, "scripts", "ci", "scan-images.sh");
        var content = File.ReadAllText(scanScript);

        Assert.Contains("pass_with_exceptions", content, StringComparison.Ordinal);
        Assert.Contains("scan_error", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Falha_de_execucao_do_scanner_nunca_deve_gravar_um_veredito_de_sucesso()
    {
        var scanScript = Path.Combine(RepositoryRoot, "scripts", "ci", "scan-images.sh");
        var content = File.ReadAllText(scanScript);

        // O ramo executado quando "docker run" do Trivy falha (ou não
        // produz relatório) tem que gravar verdict=scan_error - nunca
        // "pass" - antes de qualquer decisão de política ser tomada.
        // "(?:\w+=\S+\s+)?" tolera o prefixo opcional "MSYS_NO_PATHCONV=1 "
        // (achado real do Windows/Git Bash - ver comentário no próprio
        // scan-images.sh) sem exigir a variável de ambiente literal aqui,
        // já que o que importa é o ramo de falha em si, não essa correção
        // específica de plataforma.
        var failureBranchMatch = Regex.Match(
            content,
            @"if\s+!\s+(?:\w+=\S+\s+)?docker run.*?continue\s*\n\s*fi",
            RegexOptions.Singleline);

        Assert.True(failureBranchMatch.Success, "scan-images.sh: não foi possível localizar o ramo de falha de execução do scanner.");
        Assert.Contains("\"verdict\": \"scan_error\"", failureBranchMatch.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("\"verdict\": \"pass\"", failureBranchMatch.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Digest_do_scanner_Trivy_deve_ser_identico_em_todos_os_scripts_e_no_validador()
    {
        var scriptsDirectory = Path.Combine(RepositoryRoot, "scripts", "ci");
        var digestPattern = new Regex(@"sha256:[0-9a-f]{64}", RegexOptions.Compiled);

        var digestsFound = new HashSet<string>();
        foreach (var file in new[] { "generate-sboms.sh", "scan-images.sh", "validate-supply-chain-artifacts.sh" })
        {
            var path = Path.Combine(scriptsDirectory, file);
            Assert.True(File.Exists(path), $"scripts/ci/{file} deve existir.");
            var content = File.ReadAllText(path);
            var match = digestPattern.Match(content);
            Assert.True(match.Success, $"scripts/ci/{file} deve referenciar o digest do Trivy.");
            digestsFound.Add(match.Value);
        }

        Assert.True(digestsFound.Count == 1, $"O digest do Trivy deve ser idêntico em generate-sboms.sh, scan-images.sh e validate-supply-chain-artifacts.sh - encontrados: {string.Join(", ", digestsFound)}.");
    }

    [Fact]
    public void Excecoes_de_vulnerabilidade_devem_ter_todos_os_campos_obrigatorios()
    {
        var exceptionsFile = Path.Combine(RepositoryRoot, "docs", "security", "excecoes-de-vulnerabilidade.json");
        Assert.True(File.Exists(exceptionsFile), "docs/security/excecoes-de-vulnerabilidade.json deve existir.");

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(exceptionsFile));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, document.RootElement.ValueKind);

        string[] requiredFields =
        [
            "id", "vulnerabilityIds", "packages", "affectedImages", "rationale",
            "applicability", "compensatingControls", "owner", "created",
            "reviewDate", "expires", "status", "remediationCondition", "source"
        ];

        foreach (var exception in document.RootElement.EnumerateArray())
        {
            var id = exception.TryGetProperty("id", out var idProp) ? idProp.GetString() : "?";
            foreach (var field in requiredFields)
            {
                Assert.True(
                    exception.TryGetProperty(field, out var value) && value.ValueKind != System.Text.Json.JsonValueKind.Null,
                    $"Exceção '{id}' deve ter o campo obrigatório '{field}'.");
            }
        }
    }

    [Fact]
    public void Evidencia_gerada_de_supply_chain_deve_permanecer_ignorada_pelo_git()
    {
        var gitignore = File.ReadAllText(Path.Combine(RepositoryRoot, ".gitignore"));
        Assert.Contains("/artifacts/", gitignore, StringComparison.Ordinal);

        var tracked = ListTrackedFiles();
        Assert.DoesNotContain(tracked, path => path.StartsWith("artifacts/", StringComparison.Ordinal));
    }

    // --- Governanca do workflow de publicacao de imagens (ADR-0013).
    // Reaproveita os helpers de parsing YAML ja existentes. ---

    private static string PublishImagesWorkflowFile =>
        ListWorkflowFiles().Single(x => Path.GetFileName(x) == "publish-images.yml");

    [Fact]
    public void Workflow_de_publicacao_nao_deve_disparar_em_pull_request()
    {
        var root = LoadRootMapping(PublishImagesWorkflowFile);
        Assert.False(
            HasPullRequestTrigger(root),
            "publish-images.yml nao deve disparar em pull_request - publicacao de imagem nunca deve ser acionada por um PR.");
    }

    [Fact]
    public void Workflow_de_publicacao_deve_ser_exclusivamente_manual_via_workflow_dispatch()
    {
        // Higiene pos-merge: a configuracao anterior ("on: push: branches:
        // [main]") disparou automaticamente apos o merge da PR e falhou de
        // forma limpa e segura no preflight (nenhuma variavel de repositorio
        // configurada, nenhuma chamada AWS) - mas transformava todo push em
        // main num "falso incidente" de workflow vermelho enquanto o
        // ambiente real de publicacao AWS permanece intencionalmente nao
        // configurado. Corrigido para disparo exclusivamente manual.
        var root = LoadRootMapping(PublishImagesWorkflowFile);
        var onNode = GetChild(root, "on");
        var onMapping = Assert.IsType<YamlMappingNode>(onNode);

        Assert.NotNull(GetChild(onMapping, "workflow_dispatch"));
        Assert.Null(GetChild(onMapping, "push"));
        Assert.Single(onMapping.Children);
    }

    [Fact]
    public void Workflow_de_publicacao_nunca_deve_cancelar_uma_publicacao_em_andamento()
    {
        var root = LoadRootMapping(PublishImagesWorkflowFile);
        var concurrency = Assert.IsType<YamlMappingNode>(GetChild(root, "concurrency"));
        var cancelInProgress = GetScalarValue(concurrency, "cancel-in-progress");

        Assert.Equal("false", cancelInProgress);
    }

    [Fact]
    public void Job_de_publicacao_deve_escopar_id_token_write_somente_a_si_mesmo()
    {
        var root = LoadRootMapping(PublishImagesWorkflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));

        foreach (var jobEntry in jobs.Children)
        {
            var job = Assert.IsType<YamlMappingNode>(jobEntry.Value);
            if (GetChild(job, "permissions") is not YamlMappingNode jobPermissions)
            {
                continue;
            }

            var idToken = GetScalarValue(jobPermissions, "id-token");
            if (idToken is not null)
            {
                Assert.Equal("write", idToken);
            }
        }
    }

    [Fact]
    public void Job_de_publicacao_deve_solicitar_permissao_de_atestacao_e_nada_alem_das_4_esperadas()
    {
        // ADR-0013: a atestação real (proveniência + SBOM associadas ao
        // digest ECR) deixou de ser adiada - o job precisa de
        // exatamente contents:read, id-token:write, attestations:write
        // (nunca packages:write, específico do GHCR abandonado).
        var root = LoadRootMapping(PublishImagesWorkflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var job = Assert.IsType<YamlMappingNode>(GetChild(jobs, "build-scan-publish"));
        var permissions = Assert.IsType<YamlMappingNode>(GetChild(job, "permissions"));

        var actual = permissions.Children.Keys.Cast<YamlScalarNode>().Select(k => k.Value).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "attestations", "contents", "id-token" }, actual);
        Assert.Equal("write", GetScalarValue(permissions, "attestations"));
        Assert.Equal("write", GetScalarValue(permissions, "id-token"));
        Assert.Equal("read", GetScalarValue(permissions, "contents"));
    }

    [Fact]
    public void Workflow_de_publicacao_nao_deve_usar_credenciais_estaticas_da_AWS()
    {
        var content = File.ReadAllText(PublishImagesWorkflowFile);

        Assert.DoesNotContain("aws-access-key-id", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aws-secret-access-key", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("role-to-assume", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Pipeline_de_publicacao_deve_validar_antes_de_autenticar_na_AWS_e_publicar_depois_de_validar()
    {
        // Ordem exigida (build once -> validar -> publicar): o scan de
        // vulnerabilidade e a validacao de evidencias tem que aparecer ANTES
        // da configuracao de credenciais AWS, que por sua vez tem que
        // aparecer ANTES do script de publicacao real.
        var content = File.ReadAllText(PublishImagesWorkflowFile);

        var buildIndex = content.IndexOf("build-images-for-supply-chain.sh", StringComparison.Ordinal);
        var scanIndex = content.IndexOf("scan-images.sh", StringComparison.Ordinal);
        var validateSupplyChainIndex = content.IndexOf("validate-supply-chain-artifacts.sh", StringComparison.Ordinal);
        var generateManifestIndex = content.IndexOf("generate-release-manifest.sh", StringComparison.Ordinal);
        var validateManifestIndex = content.IndexOf("validate-release-manifest.sh", StringComparison.Ordinal);
        var configureCredentialsIndex = content.IndexOf("configure-aws-credentials", StringComparison.Ordinal);
        var publishIndex = content.IndexOf("publish-validated-images.sh", StringComparison.Ordinal);

        Assert.True(buildIndex >= 0 && scanIndex >= 0 && validateSupplyChainIndex >= 0
            && generateManifestIndex >= 0 && validateManifestIndex >= 0
            && configureCredentialsIndex >= 0 && publishIndex >= 0,
            "publish-images.yml deve conter todos os passos esperados do pipeline build-once -> validar -> publicar.");

        Assert.True(buildIndex < scanIndex, "build deve ocorrer antes do scan.");
        Assert.True(scanIndex < validateSupplyChainIndex, "scan deve ocorrer antes da validacao de evidencias.");
        Assert.True(validateSupplyChainIndex < generateManifestIndex, "evidencias devem ser validadas antes de gerar o manifesto de release.");
        Assert.True(generateManifestIndex < validateManifestIndex, "o manifesto deve ser validado logo apos ser gerado.");
        Assert.True(validateManifestIndex < configureCredentialsIndex, "o manifesto (estado pending) deve ser validado ANTES de autenticar na AWS.");
        Assert.True(configureCredentialsIndex < publishIndex, "a autenticacao na AWS deve ocorrer antes do script de publicacao.");
    }

    [Fact]
    public void Script_de_geracao_do_manifesto_deve_bloquear_publicacao_para_veredito_fail_ou_scan_error()
    {
        var scriptPath = Path.Combine(RepositoryRoot, "scripts", "ci", "generate-release-manifest.sh");
        var content = File.ReadAllText(scriptPath);

        Assert.Contains("\"fail\", \"scan_error\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_de_publicacao_nao_deve_reconstruir_imagens()
    {
        var scriptPath = Path.Combine(RepositoryRoot, "scripts", "ci", "publish-validated-images.sh");
        var content = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("docker build", content, StringComparison.Ordinal);
        Assert.Contains("docker image inspect", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_de_verificacao_nonroot_de_publicacao_nao_deve_reconstruir_imagens()
    {
        var scriptPath = Path.Combine(RepositoryRoot, "scripts", "ci", "verify-nonroot-from-manifest.sh");
        var content = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("docker build", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Tag_canonica_de_publicacao_deve_usar_a_SHA_completa_de_origem()
    {
        var scriptPath = Path.Combine(RepositoryRoot, "scripts", "ci", "generate-release-manifest.sh");
        var content = File.ReadAllText(scriptPath);

        Assert.Contains("canonical_tag = f\"sha-{source_commit}\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifesto_de_release_nunca_deve_gravar_digest_remoto_antes_de_publicar()
    {
        var scriptPath = Path.Combine(RepositoryRoot, "scripts", "ci", "generate-release-manifest.sh");
        var content = File.ReadAllText(scriptPath);

        // Campo renomeado na auditoria do Bloco 3 (remoteDigest -> remoteEcrDigest)
        // para deixar explicito que este e o digest reportado PELO ECR, nunca
        // confundido com localImageId/registryManifestDigest genericos.
        Assert.Contains("\"remoteEcrDigest\": None", content, StringComparison.Ordinal);
        Assert.Contains("\"publicationStatus\": \"pending\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifesto_de_release_deve_registrar_layerDigests_para_verificacao_de_identidade_de_conteudo()
    {
        // layerDigests (RootFS.Layers) e a identidade de conteudo portavel
        // entre image stores, usada por publish-validated-images.sh para
        // decidir idempotencia sem comparar localImageId com o digest remoto
        // do ECR (auditoria Bloco 3 - ver test-generic-oci-publish-proof.sh).
        var generateScript = Path.Combine(RepositoryRoot, "scripts", "ci", "generate-release-manifest.sh");
        var generateContent = File.ReadAllText(generateScript);
        Assert.Contains("RootFS.Layers", generateContent, StringComparison.Ordinal);
        Assert.Contains("\"layerDigests\": layer_digests", generateContent, StringComparison.Ordinal);

        var validateScript = Path.Combine(RepositoryRoot, "scripts", "ci", "validate-release-manifest.sh");
        var validateContent = File.ReadAllText(validateScript);
        Assert.Contains("layerDigests", validateContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Validador_de_manifesto_deve_suportar_os_5_estados_de_publicacao_por_componente()
    {
        var validateScript = Path.Combine(RepositoryRoot, "scripts", "ci", "validate-release-manifest.sh");
        var content = File.ReadAllText(validateScript);

        Assert.Contains(
            "VALID_COMPONENT_STATUSES = (\"pending\", \"already_published\", \"published\", \"conflict\", \"failed\")",
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validador_de_manifesto_nao_deve_aceitar_overallPublicationStatus_published_com_publicacao_parcial()
    {
        // A mentira mais perigosa que o manifesto poderia contar: declarar
        // "published" no topo quando so parte dos 4 componentes realmente
        // publicou. O validador precisa recalcular o estado esperado a
        // partir dos status individuais e comparar, nunca confiar apenas no
        // campo de topo declarado pelo script de publicacao.
        var validateScript = Path.Combine(RepositoryRoot, "scripts", "ci", "validate-release-manifest.sh");
        var content = File.ReadAllText(validateScript);

        Assert.Contains("expected_overall = \"failed\"", content, StringComparison.Ordinal);
        Assert.Contains("expected_overall = \"published\"", content, StringComparison.Ordinal);
        Assert.Contains("expected_overall = \"partial\"", content, StringComparison.Ordinal);
        Assert.Contains("if overall_status != expected_overall:", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_de_publicacao_nunca_deve_tentar_push_em_uma_tag_ja_existente()
    {
        // Auditoria Bloco 3: a documentacao oficial do ECR so confirma
        // ImageTagAlreadyExistsException ao publicar numa tag ja existente -
        // nao confirma um caminho de no-op para conteudo identico. O script
        // tem que consultar describe-images ANTES de qualquer "docker push",
        // e o ramo de tag existente nunca deve conter um "docker push".
        var scriptPath = Path.Combine(RepositoryRoot, "scripts", "ci", "publish-validated-images.sh");
        var content = File.ReadAllText(scriptPath).Replace("\r\n", "\n", StringComparison.Ordinal);

        // Busca pelo comando real de push (nao pela mencao a "docker push"
        // dentro do comentario de auditoria no topo do arquivo, que descreve
        // o comportamento da versao ANTERIOR corrigida por este bloco).
        var describeIndex = content.IndexOf("aws ecr describe-images", StringComparison.Ordinal);
        var firstPushIndex = content.IndexOf("docker push \"$REMOTE_TAG_REF\"", StringComparison.Ordinal);
        Assert.True(describeIndex >= 0 && firstPushIndex >= 0, "publish-validated-images.sh deve conter describe-images e docker push.");
        Assert.True(describeIndex < firstPushIndex, "describe-images deve ocorrer ANTES de qualquer docker push.");

        var existingTagBranchStart = content.IndexOf("JA EXISTE em", StringComparison.Ordinal);
        Assert.True(existingTagBranchStart >= 0, "ramo de tag ja existente nao encontrado.");
        var existingTagBranchEnd = content.IndexOf("\n  fi\n", existingTagBranchStart, StringComparison.Ordinal);
        Assert.True(existingTagBranchEnd > existingTagBranchStart, "fim do ramo de tag ja existente nao encontrado.");

        var existingTagBranch = content[existingTagBranchStart..existingTagBranchEnd];
        Assert.DoesNotContain("docker push", existingTagBranch, StringComparison.Ordinal);
        Assert.Contains("docker pull", existingTagBranch, StringComparison.Ordinal);
        Assert.Contains("RootFS.Layers", existingTagBranch, StringComparison.Ordinal);
        Assert.Contains("already_published", existingTagBranch, StringComparison.Ordinal);
        Assert.Contains("conflict", existingTagBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_de_publicacao_deve_falhar_ao_final_quando_algum_componente_tiver_conflito()
    {
        var scriptPath = Path.Combine(RepositoryRoot, "scripts", "ci", "publish-validated-images.sh");
        var content = File.ReadAllText(scriptPath);

        // O caminho do marcador de falha e derivado de MANIFEST_FILE (nao
        // mais hardcoded em "artifacts/release/") para permitir testes
        // isolados via RELEASE_MANIFEST_FILE (scripts/ci/test-publish-idempotency.sh)
        // sem tocar em artifacts/ real - ver auditoria do Bloco 3.
        Assert.Contains(".publish-failed", content, StringComparison.Ordinal);
        Assert.Contains("FAILURE_MARKER=\"$(dirname \"$MANIFEST_FILE\")/.publish-failed\"", content, StringComparison.Ordinal);
        Assert.Contains("if [ -f \"$FAILURE_MARKER\" ]; then", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Preflight_de_release_deve_validar_evento_ref_autorizado_antes_de_qualquer_chamada_AWS()
    {
        var preflightScript = Path.Combine(RepositoryRoot, "scripts", "ci", "check-release-prerequisites.sh");
        Assert.True(File.Exists(preflightScript), "scripts/ci/check-release-prerequisites.sh deve existir (preflight de publicacao, Bloco 3 - auditoria).");

        var content = File.ReadAllText(preflightScript);

        // O preflight nunca deve chamar a AWS - ele so valida formato
        // estatico de variaveis e o ref do evento, ANTES de qualquer
        // autenticacao via OIDC (a role so autoriza refs/heads/main).
        Assert.DoesNotContain("aws ecr", content, StringComparison.Ordinal);
        Assert.DoesNotContain("aws sts", content, StringComparison.Ordinal);
        Assert.DoesNotContain("aws iam", content, StringComparison.Ordinal);

        Assert.Contains("AUTHORIZED_REF=\"refs/heads/main\"", content, StringComparison.Ordinal);
        Assert.Contains("GITHUB_REF", content, StringComparison.Ordinal);
        Assert.Contains("GITHUB_EVENT_NAME", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Passo_de_preflight_deve_ocorrer_antes_do_build_e_expor_evento_ref_como_env()
    {
        var root = LoadRootMapping(PublishImagesWorkflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var job = Assert.IsType<YamlMappingNode>(GetChild(jobs, "build-scan-publish"));
        var steps = Assert.IsType<YamlSequenceNode>(GetChild(job, "steps"));

        var preflightStep = steps
            .Select(s => (YamlMappingNode)s)
            .SingleOrDefault(s => (GetScalarValue(s, "run") ?? string.Empty).Contains("check-release-prerequisites.sh", StringComparison.Ordinal));

        Assert.True(preflightStep is not null, "publish-images.yml deve conter um passo que rode check-release-prerequisites.sh.");
        var env = Assert.IsType<YamlMappingNode>(GetChild(preflightStep!, "env"));

        Assert.NotNull(GetChild(env, "GITHUB_EVENT_NAME"));
        Assert.NotNull(GetChild(env, "GITHUB_REF"));

        var content = File.ReadAllText(PublishImagesWorkflowFile);
        // Busca pelo "run:" real do passo (nao pela mencao ao nome do script
        // no comentario descritivo do job, que cita ambos os scripts antes
        // de qualquer passo real existir).
        var preflightIndex = content.IndexOf("run: sh scripts/ci/check-release-prerequisites.sh", StringComparison.Ordinal);
        var buildIndex = content.IndexOf("run: sh scripts/ci/build-images-for-supply-chain.sh", StringComparison.Ordinal);
        Assert.True(preflightIndex >= 0 && buildIndex >= 0 && preflightIndex < buildIndex,
            "o preflight de evento/ref deve ocorrer ANTES do build de imagens (rejeitar workflow_dispatch fora de main antes de qualquer trabalho caro).");
    }

    [Fact]
    public void Upload_do_manifesto_de_release_so_deve_rodar_quando_o_arquivo_existir_e_deve_manter_erro_se_ausente_apos_rodar()
    {
        // Quando o preflight falha (variaveis de repositorio ausentes), o
        // manifesto nunca chega a ser gerado - "if: always()" sozinho
        // reexecutaria este passo de upload mesmo assim, produzindo um
        // segundo erro ("arquivo ausente") que mascara a causa real
        // (preflight). A condicao precisa checar hashFiles() antes de
        // tentar o upload; quando o passo de fato roda, o manifesto tem que
        // existir - entao "if-no-files-found: error" continua correto ali.
        var root = LoadRootMapping(PublishImagesWorkflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var job = Assert.IsType<YamlMappingNode>(GetChild(jobs, "build-scan-publish"));
        var steps = Assert.IsType<YamlSequenceNode>(GetChild(job, "steps"));

        var uploadStep = steps
            .Select(s => (YamlMappingNode)s)
            .SingleOrDefault(s => (GetScalarValue(s, "name") ?? string.Empty)
                .Contains("manifesto de release como evidencia", StringComparison.OrdinalIgnoreCase));

        Assert.True(uploadStep is not null, "publish-images.yml deve conter o passo de upload do release-manifest como evidencia do workflow.");

        var ifCondition = GetScalarValue(uploadStep!, "if") ?? string.Empty;
        Assert.Contains("always()", ifCondition, StringComparison.Ordinal);
        Assert.Contains("hashFiles('artifacts/release/release-manifest.json')", ifCondition, StringComparison.Ordinal);
        Assert.Contains("!= ''", ifCondition, StringComparison.Ordinal);

        var with = Assert.IsType<YamlMappingNode>(GetChild(uploadStep!, "with"));
        Assert.Equal("error", GetScalarValue(with, "if-no-files-found"));
    }

    [Fact]
    public void Deploy_Development_deve_continuar_disparado_pela_conclusao_do_Publish_Images()
    {
        var deployWorkflow = ListWorkflowFiles().Single(x => Path.GetFileName(x) == "deploy-development.yml");
        var root = LoadRootMapping(deployWorkflow);
        var onMapping = Assert.IsType<YamlMappingNode>(GetChild(root, "on"));
        var workflowRun = Assert.IsType<YamlMappingNode>(GetChild(onMapping, "workflow_run"));

        var workflowsNode = Assert.IsType<YamlSequenceNode>(GetChild(workflowRun, "workflows"));
        var workflowNames = workflowsNode.Select(n => ((YamlScalarNode)n).Value).ToArray();
        Assert.Contains("Publish Images", workflowNames);

        var typesNode = Assert.IsType<YamlSequenceNode>(GetChild(workflowRun, "types"));
        var types = typesNode.Select(n => ((YamlScalarNode)n).Value).ToArray();
        Assert.Contains("completed", types);
    }

    [Fact]
    public void Deploy_Development_so_deve_rodar_o_job_de_deploy_quando_a_publicacao_concluir_com_sucesso()
    {
        var deployWorkflow = ListWorkflowFiles().Single(x => Path.GetFileName(x) == "deploy-development.yml");
        var root = LoadRootMapping(deployWorkflow);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var deployJob = Assert.IsType<YamlMappingNode>(GetChild(jobs, "deploy"));

        var ifCondition = GetScalarValue(deployJob, "if");
        Assert.Equal("github.event.workflow_run.conclusion == 'success'", ifCondition);
    }

    private static IReadOnlyCollection<string> ListTrackedFiles()
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git", "ls-files")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IEnumerable<string> FindAllUsesValues(string workflowFile)
    {
        var root = LoadRootMapping(workflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));

        foreach (var jobEntry in jobs.Children)
        {
            var job = Assert.IsType<YamlMappingNode>(jobEntry.Value);
            if (GetChild(job, "steps") is not YamlSequenceNode steps)
            {
                continue;
            }

            foreach (var stepNode in steps)
            {
                var step = Assert.IsType<YamlMappingNode>(stepNode);
                if (GetScalarValue(step, "uses") is { } usesValue)
                {
                    yield return usesValue;
                }
            }
        }
    }

    private static YamlMappingNode LoadRootMapping(string workflowFile)
    {
        var yaml = new YamlStream();
        using var reader = new StreamReader(workflowFile);
        yaml.Load(reader);

        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }

    private static YamlNode? GetChild(YamlMappingNode mapping, string key)
    {
        return mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    }

    private static string? GetScalarValue(YamlMappingNode mapping, string key)
    {
        return GetChild(mapping, key) is YamlScalarNode scalar ? scalar.Value : null;
    }

    private static IReadOnlyCollection<string> ListWorkflowFiles()
    {
        var workflowsDirectory = Path.Combine(RepositoryRoot, ".github", "workflows");

        return Directory.Exists(workflowsDirectory)
            ? Directory.GetFiles(workflowsDirectory, "*.yml").Concat(Directory.GetFiles(workflowsDirectory, "*.yaml")).ToArray()
            : [];
    }

    private static string RelativePath(string fullPath)
    {
        return Path.GetRelativePath(RepositoryRoot, fullPath).Replace('\\', '/');
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

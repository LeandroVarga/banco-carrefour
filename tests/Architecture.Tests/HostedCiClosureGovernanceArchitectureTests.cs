using Xunit;
using YamlDotNet.RepresentationModel;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Regressão dos defeitos reais encontrados na primeira execução hospedada
/// do CI/Supply Chain no GitHub Actions: Architecture.Tests sem git/python3
/// reais no container-irmão, isolamento de filesystem entre jobs do Supply
/// Chain, terminologia de imagens e permissões mínimas do Dependency
/// Review.
/// </summary>
public sealed class HostedCiClosureGovernanceArchitectureTests
{
    [Fact]
    public void Testes_de_arquitetura_no_CI_devem_rodar_nativamente_com_NET_configurado()
    {
        var root = LoadRootMapping(CiWorkflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var fastQualityGate = Assert.IsType<YamlMappingNode>(GetChild(jobs, "fast-quality-gate"));
        var steps = Assert.IsType<YamlSequenceNode>(GetChild(fastQualityGate, "steps"));

        var stepList = steps.Select(s => Assert.IsType<YamlMappingNode>(s)).ToList();

        var setupDotnet = stepList.FirstOrDefault(s => GetScalarValue(s, "uses")?.StartsWith("actions/setup-dotnet@", StringComparison.Ordinal) == true);
        Assert.True(setupDotnet is not null, "fast-quality-gate deve configurar o .NET SDK nativamente no runner (actions/setup-dotnet) para os testes de arquitetura.");

        var architectureStep = stepList.FirstOrDefault(s => (GetScalarValue(s, "name") ?? "").Contains("Testes de arquitetura", StringComparison.Ordinal));
        Assert.True(architectureStep is not null, "fast-quality-gate deve ter um step de 'Testes de arquitetura'.");

        var runValue = GetScalarValue(architectureStep!, "run") ?? "";
        Assert.DoesNotContain("docker compose run", runValue, StringComparison.Ordinal);
        Assert.Contains("dotnet test tests/Architecture.Tests/Architecture.Tests.csproj", runValue, StringComparison.Ordinal);

        var setupDotnetIndex = stepList.IndexOf(setupDotnet!);
        var architectureStepIndex = stepList.IndexOf(architectureStep!);
        Assert.True(setupDotnetIndex < architectureStepIndex, "actions/setup-dotnet deve vir antes do step de testes de arquitetura.");
    }

    [Fact]
    public void Preflight_de_git_e_python_deve_existir_antes_dos_testes_de_arquitetura()
    {
        var root = LoadRootMapping(CiWorkflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var fastQualityGate = Assert.IsType<YamlMappingNode>(GetChild(jobs, "fast-quality-gate"));
        var steps = Assert.IsType<YamlSequenceNode>(GetChild(fastQualityGate, "steps"));
        var stepList = steps.Select(s => Assert.IsType<YamlMappingNode>(s)).ToList();

        var preflightStep = stepList.FirstOrDefault(s => (GetScalarValue(s, "name") ?? "").Contains("Preflight", StringComparison.Ordinal));
        Assert.True(preflightStep is not null, "fast-quality-gate deve ter um step de preflight que confirme git/python3/dotnet antes dos testes de arquitetura.");

        var preflightRun = GetScalarValue(preflightStep!, "run") ?? "";
        Assert.Contains("git --version", preflightRun, StringComparison.Ordinal);
        Assert.Contains("python3 --version", preflightRun, StringComparison.Ordinal);
        Assert.Contains("dotnet --info", preflightRun, StringComparison.Ordinal);
        Assert.Contains("git ls-files", preflightRun, StringComparison.Ordinal);

        var architectureStep = stepList.First(s => (GetScalarValue(s, "name") ?? "").Contains("Testes de arquitetura", StringComparison.Ordinal));
        Assert.True(
            stepList.IndexOf(preflightStep!) < stepList.IndexOf(architectureStep),
            "o preflight deve ocorrer ANTES dos testes de arquitetura.");
    }

    [Fact]
    public void Testes_de_arquitetura_nunca_devem_ser_tolerantes_a_falha_ou_filtrados()
    {
        var root = LoadRootMapping(CiWorkflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var fastQualityGate = Assert.IsType<YamlMappingNode>(GetChild(jobs, "fast-quality-gate"));
        var steps = Assert.IsType<YamlSequenceNode>(GetChild(fastQualityGate, "steps"));

        foreach (var stepNode in steps)
        {
            var step = Assert.IsType<YamlMappingNode>(stepNode);
            var name = GetScalarValue(step, "name") ?? "";
            if (!name.Contains("Testes de arquitetura", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Null(GetScalarValue(step, "continue-on-error"));
            var runValue = GetScalarValue(step, "run") ?? "";
            Assert.DoesNotContain("--filter", runValue, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Job_de_dependency_review_deve_ter_permissoes_minimas_incluindo_pull_requests_write()
    {
        var root = LoadRootMapping(SupplyChainWorkflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var dependencyReview = Assert.IsType<YamlMappingNode>(GetChild(jobs, "dependency-review"));
        var permissions = Assert.IsType<YamlMappingNode>(GetChild(dependencyReview, "permissions"));

        Assert.Equal("read", GetScalarValue(permissions, "contents"));
        Assert.Equal("write", GetScalarValue(permissions, "pull-requests"));
        Assert.Equal(2, permissions.Children.Count);

        var steps = Assert.IsType<YamlSequenceNode>(GetChild(dependencyReview, "steps"));
        var dependencyReviewStep = steps
            .Select(s => Assert.IsType<YamlMappingNode>(s))
            .Single(s => (GetScalarValue(s, "uses") ?? "").StartsWith("actions/dependency-review-action@", StringComparison.Ordinal));

        var with = Assert.IsType<YamlMappingNode>(GetChild(dependencyReviewStep, "with"));
        Assert.Equal("high", GetScalarValue(with, "fail-on-severity"));
        Assert.Equal("always", GetScalarValue(with, "comment-summary-in-pr"));
    }

    [Fact]
    public void Validacao_agregada_de_supply_chain_deve_depender_dos_dois_jobs_produtores_de_evidencia()
    {
        var root = LoadRootMapping(SupplyChainWorkflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var aggregate = Assert.IsType<YamlMappingNode>(GetChild(jobs, "supply-chain-evidence-validation"));

        var needs = GetChild(aggregate, "needs");
        var needsList = needs switch
        {
            YamlSequenceNode seq => seq.Select(n => ((YamlScalarNode)n).Value!).ToArray(),
            YamlScalarNode scalar => [scalar.Value!],
            _ => throw new Xunit.Sdk.XunitException("'needs' do job agregado deve ser uma lista ou escalar."),
        };

        Assert.Contains("dependency-audit", needsList);
        Assert.Contains("image-sbom-and-scan", needsList);
    }

    [Fact]
    public void Nomes_de_artifact_de_upload_e_download_do_supply_chain_devem_corresponder_exatamente()
    {
        var root = LoadRootMapping(SupplyChainWorkflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));

        var uploadNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var jobName in new[] { "dependency-audit", "image-sbom-and-scan" })
        {
            var job = Assert.IsType<YamlMappingNode>(GetChild(jobs, jobName));
            var steps = Assert.IsType<YamlSequenceNode>(GetChild(job, "steps"));
            foreach (var stepNode in steps)
            {
                var step = Assert.IsType<YamlMappingNode>(stepNode);
                if ((GetScalarValue(step, "uses") ?? "").StartsWith("actions/upload-artifact@", StringComparison.Ordinal)
                    && GetChild(step, "with") is YamlMappingNode with
                    && GetScalarValue(with, "name") is { } name)
                {
                    uploadNames.Add(name);
                }
            }
        }

        var aggregate = Assert.IsType<YamlMappingNode>(GetChild(jobs, "supply-chain-evidence-validation"));
        var aggregateSteps = Assert.IsType<YamlSequenceNode>(GetChild(aggregate, "steps"));
        var downloadNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stepNode in aggregateSteps)
        {
            var step = Assert.IsType<YamlMappingNode>(stepNode);
            if ((GetScalarValue(step, "uses") ?? "").StartsWith("actions/download-artifact@", StringComparison.Ordinal)
                && GetChild(step, "with") is YamlMappingNode with
                && GetScalarValue(with, "name") is { } name)
            {
                downloadNames.Add(name);
            }
        }

        Assert.Equal(uploadNames, downloadNames);
        Assert.Equal(3, uploadNames.Count);
    }

    [Fact]
    public void Job_de_imagens_do_Supply_Chain_nao_deve_assumir_filesystem_do_job_de_auditoria_NuGet()
    {
        var root = LoadRootMapping(SupplyChainWorkflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var imageJob = Assert.IsType<YamlMappingNode>(GetChild(jobs, "image-sbom-and-scan"));
        var steps = Assert.IsType<YamlSequenceNode>(GetChild(imageJob, "steps"));

        var validateStep = steps
            .Select(s => Assert.IsType<YamlMappingNode>(s))
            .Single(s => (GetScalarValue(s, "run") ?? "").Contains("validate-supply-chain-artifacts.sh", StringComparison.Ordinal));

        var env = Assert.IsType<YamlMappingNode>(GetChild(validateStep, "env"));
        Assert.Equal("1", GetScalarValue(env, "SUPPLY_CHAIN_SKIP_NUGET_CHECK"));

        // O job agregado, ao contrario, precisa validar a auditoria NuGet -
        // nunca deve pular essa secao (senao a evidencia baixada nunca e
        // realmente conferida).
        var aggregate = Assert.IsType<YamlMappingNode>(GetChild(jobs, "supply-chain-evidence-validation"));
        var aggregateSteps = Assert.IsType<YamlSequenceNode>(GetChild(aggregate, "steps"));
        var aggregateValidateStep = aggregateSteps
            .Select(s => Assert.IsType<YamlMappingNode>(s))
            .Single(s => (GetScalarValue(s, "run") ?? "").Contains("validate-supply-chain-artifacts.sh", StringComparison.Ordinal));
        Assert.Null(GetChild(aggregateValidateStep, "env"));
    }

    [Fact]
    public void Workflows_de_imagem_devem_descrever_5_imagens_e_nunca_4_imagens()
    {
        foreach (var workflowFile in new[] { CiWorkflowFile, SupplyChainWorkflowFile })
        {
            var content = File.ReadAllText(workflowFile);
            Assert.DoesNotContain("4 imagens", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("4 images", content, StringComparison.OrdinalIgnoreCase);
        }

        var supplyChainContent = File.ReadAllText(SupplyChainWorkflowFile);
        Assert.Contains("5 imagens", supplyChainContent, StringComparison.Ordinal);

        var ciContent = File.ReadAllText(CiWorkflowFile);
        Assert.Contains("5 imagens", ciContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflows_de_imagem_devem_manter_o_guard_textual_de_que_MigrationRunner_nunca_e_workload_de_negocio()
    {
        // O comentario deve conter a NEGACAO explicita ("nunca um Nº
        // workload de negocio") - nunca uma afirmacao positiva de que
        // MigrationRunner e um componente/workload de negocio.
        foreach (var workflowFile in new[] { CiWorkflowFile, SupplyChainWorkflowFile })
        {
            var normalized = NormalizeWhitespace(File.ReadAllText(workflowFile));
            Assert.Contains("nunca um 5o workload de negocio", normalized, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string NormalizeWhitespace(string content)
    {
        // Colapsa qualquer sequencia de espacos/quebras de linha (incluindo
        // as que atravessam o wrap de um comentario YAML de varias linhas,
        // cada uma prefixada por "#") num unico espaco - a asserção nunca
        // deve depender de onde o comentario foi quebrado.
        return System.Text.RegularExpressions.Regex.Replace(content, @"[\s#]+", " ").Trim();
    }

    private static string CiWorkflowFile => Path.Combine(RepositoryRoot, ".github", "workflows", "ci.yml");

    private static string SupplyChainWorkflowFile => Path.Combine(RepositoryRoot, ".github", "workflows", "supply-chain.yml");

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

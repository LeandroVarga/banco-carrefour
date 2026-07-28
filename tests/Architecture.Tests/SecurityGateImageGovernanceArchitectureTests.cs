using Xunit;
using YamlDotNet.RepresentationModel;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Regressão do defeito real encontrado na execução hospedada do Security
/// gate: GitHub Actions executa cada job em runner isolado, sem
/// compartilhar filesystem nem daemon Docker com o job "image-gate" - a
/// AuthenticatedEdgeFlowFixture (tests/Security.IntegrationTests/Edge)
/// precisa das imagens locais <c>banco-carrefour-ledger-api:local</c> e
/// <c>banco-carrefour-consolidation-api:local</c> já presentes no MESMO
/// job antes de subir os containers via Testcontainers.
/// </summary>
public sealed class SecurityGateImageGovernanceArchitectureTests
{
    [Fact]
    public void Security_gate_deve_buildar_ledger_api_e_consolidation_api_antes_dos_testes()
    {
        var root = LoadRootMapping(CiWorkflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var securityGate = Assert.IsType<YamlMappingNode>(GetChild(jobs, "security-gate"));
        var steps = Assert.IsType<YamlSequenceNode>(GetChild(securityGate, "steps"));
        var stepList = steps.Select(s => Assert.IsType<YamlMappingNode>(s)).ToList();

        var buildStep = stepList.FirstOrDefault(s => (GetScalarValue(s, "run") ?? "").Contains("docker compose build", StringComparison.Ordinal));
        Assert.True(buildStep is not null, "security-gate deve buildar as imagens locais exigidas pela AuthenticatedEdgeFlowFixture antes dos testes.");
        var buildRun = GetScalarValue(buildStep!, "run") ?? "";
        Assert.Contains("ledger-api", buildRun, StringComparison.Ordinal);
        Assert.Contains("consolidation-api", buildRun, StringComparison.Ordinal);

        var testStep = stepList.First(s => (GetScalarValue(s, "run") ?? "").Contains("Security.IntegrationTests.csproj", StringComparison.Ordinal));
        Assert.True(stepList.IndexOf(buildStep!) < stepList.IndexOf(testStep), "o build das imagens locais deve ocorrer antes dos testes de segurança.");
    }

    [Fact]
    public void Security_gate_deve_verificar_as_duas_imagens_locais_antes_dos_testes()
    {
        var root = LoadRootMapping(CiWorkflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var securityGate = Assert.IsType<YamlMappingNode>(GetChild(jobs, "security-gate"));
        var steps = Assert.IsType<YamlSequenceNode>(GetChild(securityGate, "steps"));
        var stepList = steps.Select(s => Assert.IsType<YamlMappingNode>(s)).ToList();

        var inspectStep = stepList.FirstOrDefault(s => (GetScalarValue(s, "run") ?? "").Contains("docker image inspect", StringComparison.Ordinal));
        Assert.True(inspectStep is not null, "security-gate deve verificar explicitamente que as imagens locais existem antes dos testes.");
        var inspectRun = GetScalarValue(inspectStep!, "run") ?? "";
        Assert.Contains("banco-carrefour-ledger-api:local", inspectRun, StringComparison.Ordinal);
        Assert.Contains("banco-carrefour-consolidation-api:local", inspectRun, StringComparison.Ordinal);

        var testStep = stepList.First(s => (GetScalarValue(s, "run") ?? "").Contains("Security.IntegrationTests.csproj", StringComparison.Ordinal));
        Assert.True(stepList.IndexOf(inspectStep!) < stepList.IndexOf(testStep), "a verificação das imagens deve ocorrer antes dos testes de segurança.");
    }

    [Fact]
    public void Testes_de_seguranca_nunca_devem_ser_tolerantes_a_falha_ou_filtrados()
    {
        var root = LoadRootMapping(CiWorkflowFile);
        var jobs = Assert.IsType<YamlMappingNode>(GetChild(root, "jobs"));
        var securityGate = Assert.IsType<YamlMappingNode>(GetChild(jobs, "security-gate"));
        var steps = Assert.IsType<YamlSequenceNode>(GetChild(securityGate, "steps"));

        foreach (var stepNode in steps)
        {
            var step = Assert.IsType<YamlMappingNode>(stepNode);
            var runValue = GetScalarValue(step, "run") ?? "";
            if (!runValue.Contains("Security.IntegrationTests.csproj", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Null(GetScalarValue(step, "continue-on-error"));
            Assert.DoesNotContain("--filter", runValue, StringComparison.Ordinal);
        }
    }

    private static string CiWorkflowFile => Path.Combine(RepositoryRoot, ".github", "workflows", "ci.yml");

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

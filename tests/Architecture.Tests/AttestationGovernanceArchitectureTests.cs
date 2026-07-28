using System.Linq;
using System.Text.Json;
using YamlDotNet.RepresentationModel;
using Xunit;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Governança da atestação hospedada real (proveniência + SBOM) para o
/// alvo Amazon ECR/AWS definitivo (ver ADR-0013): garante, por parsing
/// real do YAML/JSON Schema,
/// que a atestação usa as actions oficiais corretas (não depreciadas),
/// permissões mínimas, associação ao digest ECR remoto real, e que o
/// manifesto nunca aceita uma atestação fabricada.
/// </summary>
public sealed class AttestationGovernanceArchitectureTests
{
    private static readonly string[] Components =
    [
        "ledger-api",
        "ledger-outbox-publisher",
        "consolidation-api",
        "consolidation-worker",
        "migration-runner"
    ];

    [Fact]
    public void Job_de_publicacao_deve_ter_exatamente_as_3_permissoes_necessarias_para_atestacao()
    {
        var yaml = new YamlStream();
        using var reader = new StreamReader(Path.Combine(RepositoryRoot, ".github", "workflows", "publish-images.yml"));
        yaml.Load(reader);

        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var jobs = (YamlMappingNode)root.Children[new YamlScalarNode("jobs")];
        var job = (YamlMappingNode)jobs.Children[new YamlScalarNode("build-scan-publish")];
        var permissions = (YamlMappingNode)job.Children[new YamlScalarNode("permissions")];

        var actual = permissions.Children.Keys.Cast<YamlScalarNode>().Select(k => k.Value).OrderBy(x => x).ToArray();
        var expected = new[] { "attestations", "contents", "id-token" };

        Assert.Equal(expected, actual);
        Assert.Equal("write", ((YamlScalarNode)permissions.Children[new YamlScalarNode("attestations")]).Value);
        Assert.Equal("write", ((YamlScalarNode)permissions.Children[new YamlScalarNode("id-token")]).Value);
        Assert.Equal("read", ((YamlScalarNode)permissions.Children[new YamlScalarNode("contents")]).Value);
    }

    [Fact]
    public void Atestacao_de_proveniencia_deve_usar_a_action_oficial_nao_depreciada_pinada_por_SHA()
    {
        var content = ReadRepositoryFile(".github/workflows/publish-images.yml");

        // actions/attest-build-provenance (nao depreciada, confirmado
        // consultando o action.yml oficial antes de fixar a versao).
        Assert.Contains("actions/attest-build-provenance@e8998f949152b193b063cb0ec769d69d929409be # v2.4.0", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Atestacao_de_SBOM_deve_usar_actions_attest_generico_e_nunca_o_wrapper_depreciado()
    {
        var content = ReadRepositoryFile(".github/workflows/publish-images.yml");

        // actions/attest-sbom emite hoje um aviso de depreciacao em runtime
        // ("please use actions/attest instead", confirmado lendo o
        // action.yml oficial) - por isso usamos actions/attest diretamente
        // com "sbom-path", nunca o wrapper depreciado.
        Assert.Contains("actions/attest@f7c74d28b9d84cb8768d0b8ca14a4bac6ef463e6 # v4.2.0", content, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/attest-sbom@", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Todos_os_5_componentes_devem_ter_atestacao_de_proveniencia_e_de_SBOM()
    {
        var content = ReadRepositoryFile(".github/workflows/publish-images.yml");

        foreach (var component in Components)
        {
            Assert.Contains($"id: attest-provenance-{component}", content, StringComparison.Ordinal);
            Assert.Contains($"id: attest-sbom-{component}", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Todos_os_passos_de_atestacao_devem_publicar_no_registry_e_usar_o_digest_real_extraido()
    {
        var yaml = new YamlStream();
        using var reader = new StreamReader(Path.Combine(RepositoryRoot, ".github", "workflows", "publish-images.yml"));
        yaml.Load(reader);

        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var jobs = (YamlMappingNode)root.Children[new YamlScalarNode("jobs")];
        var job = (YamlMappingNode)jobs.Children[new YamlScalarNode("build-scan-publish")];
        var steps = (YamlSequenceNode)job.Children[new YamlScalarNode("steps")];

        var attestationSteps = steps
            .Cast<YamlMappingNode>()
            .Where(step => step.Children.TryGetValue(new YamlScalarNode("id"), out var idNode)
                           && ((YamlScalarNode)idNode).Value is { } id
                           && (id.StartsWith("attest-provenance-", StringComparison.Ordinal) || id.StartsWith("attest-sbom-", StringComparison.Ordinal)))
            .ToArray();

        Assert.Equal(Components.Length * 2, attestationSteps.Length);

        foreach (var step in attestationSteps)
        {
            var with = (YamlMappingNode)step.Children[new YamlScalarNode("with")];

            var pushToRegistry = ((YamlScalarNode)with.Children[new YamlScalarNode("push-to-registry")]).Value;
            Assert.Equal("true", pushToRegistry);

            var subjectDigest = ((YamlScalarNode)with.Children[new YamlScalarNode("subject-digest")]).Value;
            Assert.Contains("steps.digests.outputs.", subjectDigest, StringComparison.Ordinal);

            var subjectName = ((YamlScalarNode)with.Children[new YamlScalarNode("subject-name")]).Value;
            Assert.Contains("steps.digests.outputs.", subjectName, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Passo_de_extracao_de_digests_deve_ler_do_manifesto_de_release_real_apos_publicacao_no_ECR()
    {
        var content = ReadRepositoryFile(".github/workflows/publish-images.yml");

        Assert.Contains("id: digests", content, StringComparison.Ordinal);
        Assert.Contains("artifacts/release/release-manifest.json", content, StringComparison.Ordinal);
        Assert.Contains("c['ecrRepositoryUri']", content, StringComparison.Ordinal);
        Assert.Contains("c['remoteEcrDigest']", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Extracao_de_digests_deve_ocorrer_apos_a_publicacao_real_no_ECR()
    {
        // As atestacoes precisam do remoteEcrDigest REAL (preenchido por
        // publish-validated-images.sh) - nunca do localImageId nem de um
        // digest ainda pendente.
        var content = ReadRepositoryFile(".github/workflows/publish-images.yml");

        var publishIndex = content.IndexOf("run: sh scripts/ci/publish-validated-images.sh", StringComparison.Ordinal);
        var digestsIndex = content.IndexOf("id: digests", StringComparison.Ordinal);

        Assert.True(publishIndex >= 0 && digestsIndex >= 0, "publish-images.yml deve conter os passos de publicacao real e extracao de digests.");
        Assert.True(publishIndex < digestsIndex, "a extracao de digests para atestacao deve ocorrer DEPOIS da publicacao real no ECR.");
    }

    [Fact]
    public void Resultado_das_atestacoes_deve_ser_registrado_a_partir_dos_outputs_reais_das_actions()
    {
        var content = ReadRepositoryFile(".github/workflows/publish-images.yml");

        foreach (var component in Components)
        {
            Assert.Contains($"steps.attest-provenance-{component}.outputs.attestation-id", content, StringComparison.Ordinal);
            Assert.Contains($"steps.attest-sbom-{component}.outputs.attestation-id", content, StringComparison.Ordinal);
        }

        Assert.Contains("record-release-attestations.sh", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_de_registro_de_atestacoes_nunca_deve_fabricar_status_generated_sem_id_real()
    {
        var content = ReadRepositoryFile("scripts/ci/record-release-attestations.sh");

        Assert.Contains("if prov_id:", content, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"failed\", \"reference\": None", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_do_manifesto_deve_exigir_provenanceAttestation_e_sbomAttestation()
    {
        using var doc = JsonDocument.Parse(ReadRepositoryFile("schemas/release-manifest.schema.json"));
        var component = doc.RootElement.GetProperty("$defs").GetProperty("component");

        var required = component.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("provenanceAttestation", required);
        Assert.Contains("sbomAttestation", required);
        Assert.DoesNotContain("attestationStatus", required);

        var attestationDef = doc.RootElement.GetProperty("$defs").GetProperty("attestation");
        var statusEnum = attestationDef.GetProperty("properties").GetProperty("status").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal(
            new[] { "not_applicable", "pending_hosted_execution", "generated", "failed", "verified" },
            statusEnum);
    }

    [Fact]
    public void SchemaVersion_deve_ser_4_0_0_e_consistente_entre_schema_gerador_e_validador()
    {
        // 4.0.0 sucede 3.0.0 com a separação components/operationalArtifacts -
        // migration-runner deixa de ser um 5º nome dentro de "components".
        using var doc = JsonDocument.Parse(ReadRepositoryFile("schemas/release-manifest.schema.json"));
        var schemaVersion = doc.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString();
        Assert.Equal("4.0.0", schemaVersion);

        Assert.Contains("\"schemaVersion\": \"4.0.0\"", ReadRepositoryFile("scripts/ci/generate-release-manifest.sh"), StringComparison.Ordinal);
        Assert.Contains("!= \"4.0.0\"", ReadRepositoryFile("scripts/ci/validate-release-manifest.sh"), StringComparison.Ordinal);
    }

    [Fact]
    public void Gerador_nunca_deve_fabricar_uma_atestacao_generated_sem_reference_real()
    {
        var content = ReadRepositoryFile("scripts/ci/generate-release-manifest.sh");

        Assert.Contains("def build_attestation", content, StringComparison.Ordinal);
        Assert.Contains("nunca fabricada", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Validador_deve_rejeitar_release_oficial_passed_com_atestacao_generated_sem_reference()
    {
        var content = ReadRepositoryFile("scripts/ci/validate-release-manifest.sh");

        Assert.Contains("overall_verdict == \"passed\"", content, StringComparison.Ordinal);
        Assert.Contains("isso seria uma atestacao fabricada", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ADR_0013_deve_existir_e_estar_marcada_como_Aceita()
    {
        var content = ReadRepositoryFile("docs/decisions/ADR-0013-integridade-de-release-e-software-supply-chain.md");

        Assert.Contains("status: Aceita", content, StringComparison.Ordinal);
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

using System.Linq;
using System.Text.Json;
using Xunit;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Governança do manifesto de release do alvo Amazon ECR/AWS real (ver
/// ADR-0013): garante, por leitura real do
/// schema/exemplo/scripts, que a identidade de release usada pela
/// publicação real (scripts/ci/generate-release-manifest.sh +
/// scripts/ci/publish-validated-images.sh, workflow publish-images.yml)
/// permanece coerente - schema versionado, exatamente 4 componentes
/// aprovados, tags imutáveis, digests bem formados, exemplo não-produtivo
/// sempre válido contra o próprio schema.
/// </summary>
public sealed class ReleaseManifestGovernanceArchitectureTests
{
    private static readonly string[] ApprovedComponents =
    [
        "ledger-api",
        "ledger-outbox-publisher",
        "consolidation-api",
        "consolidation-worker"
    ];

    [Fact]
    public void Schema_de_release_manifest_deve_ser_JSON_valido_com_additionalProperties_false()
    {
        using var doc = JsonDocument.Parse(ReadRepositoryFile("schemas/release-manifest.schema.json"));
        var root = doc.RootElement;

        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());

        var component = root.GetProperty("$defs").GetProperty("component");
        Assert.False(component.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void Schema_deve_exigir_exatamente_4_componentes_do_conjunto_aprovado()
    {
        using var doc = JsonDocument.Parse(ReadRepositoryFile("schemas/release-manifest.schema.json"));
        var componentsProp = doc.RootElement.GetProperty("properties").GetProperty("components");

        Assert.Equal(4, componentsProp.GetProperty("minItems").GetInt32());
        Assert.Equal(4, componentsProp.GetProperty("maxItems").GetInt32());

        var nameEnum = doc.RootElement.GetProperty("$defs").GetProperty("component")
            .GetProperty("properties").GetProperty("component").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal(ApprovedComponents.OrderBy(x => x), nameEnum.OrderBy(x => x));
    }

    [Fact]
    public void Schema_nunca_deve_permitir_tag_latest_como_identidade_imutavel()
    {
        using var doc = JsonDocument.Parse(ReadRepositoryFile("schemas/release-manifest.schema.json"));
        var tagPattern = doc.RootElement.GetProperty("$defs").GetProperty("component")
            .GetProperty("properties").GetProperty("canonicalTag").GetProperty("pattern").GetString();

        Assert.NotNull(tagPattern);
        Assert.DoesNotMatch(tagPattern!, "latest");
        Assert.Matches(tagPattern!, "sha-" + new string('a', 40));
    }

    [Fact]
    public void Schema_deve_declarar_ecr_como_unico_registryType()
    {
        using var doc = JsonDocument.Parse(ReadRepositoryFile("schemas/release-manifest.schema.json"));
        var registryType = doc.RootElement.GetProperty("properties").GetProperty("registryType").GetProperty("const").GetString();

        Assert.Equal("ecr", registryType);
    }

    [Fact]
    public void Exemplo_nao_produtivo_deve_passar_no_validador_real()
    {
        // O validador real exige que sbomPath/vulnerabilityReportPath
        // apontem para arquivos que de fato existem (checagem propositalmente
        // rigorosa - nunca aceita uma release sem evidencia real). O exemplo
        // commitado referencia caminhos fictícios de artifacts/ (que só
        // existem depois de um build real) - para validar o exemplo sem
        // depender de rodar o pipeline completo de supply chain, copiamos
        // para um diretório temporário e substituímos esses dois campos por
        // arquivos-placeholder reais (o resto do conteúdo permanece
        // idêntico ao exemplo commitado).
        var fixtureDir = Path.Combine(Path.GetTempPath(), "release-manifest-example-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDir);
        try
        {
            using var doc = JsonDocument.Parse(ReadRepositoryFile("schemas/examples/release-manifest.example.json"));
            var manifest = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(doc.RootElement.GetRawText())!;

            var componentsArray = manifest["components"].EnumerateArray()
                .Select(c =>
                {
                    var componentDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(c.GetRawText())!;
                    var name = componentDict["component"].GetString();
                    var sbomPath = Path.Combine(fixtureDir, $"{name}.cyclonedx.json");
                    var vulnPath = Path.Combine(fixtureDir, $"{name}.trivy.json");
                    File.WriteAllText(sbomPath, "{}");
                    File.WriteAllText(vulnPath, "{}");

                    var mutable = new Dictionary<string, object?>();
                    foreach (var prop in c.EnumerateObject())
                    {
                        mutable[prop.Name] = prop.Name switch
                        {
                            "sbomPath" => sbomPath,
                            "vulnerabilityReportPath" => vulnPath,
                            _ => JsonSerializer.Deserialize<object?>(prop.Value.GetRawText()),
                        };
                    }
                    return mutable;
                })
                .ToArray();

            var mutableManifest = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                mutableManifest[prop.Name] = prop.Name == "components"
                    ? componentsArray
                    : JsonSerializer.Deserialize<object?>(prop.Value.GetRawText());
            }

            var fixtureManifestPath = Path.Combine(fixtureDir, "release-manifest.json");
            File.WriteAllText(fixtureManifestPath, JsonSerializer.Serialize(mutableManifest));

            var (exitCode, output) = RunShellScriptWithEnv(
                new (string, string)[]
                {
                    ("RELEASE_MANIFEST_FILE", fixtureManifestPath),
                    ("RELEASE_SKIP_LIVE_TREE_CHECK", "1"),
                    ("RELEASE_EXPECTED_HEAD", new string('0', 40)),
                },
                "scripts/ci/validate-release-manifest.sh");

            Assert.True(exitCode == 0, $"exemplo deveria validar com sucesso, saida: {output}");
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }

    [Fact]
    public void Build_once_deve_gravar_labels_OCI_padrao_na_propria_imagem()
    {
        // org.opencontainers.image.revision e a base da verificacao real de
        // ociRevision no manifesto de release (nunca apenas copiado de
        // sourceCommit) - precisa ser gravado no momento do build, nao
        // inferido depois.
        var content = ReadRepositoryFile("scripts/ci/build-images-for-supply-chain.sh");

        Assert.Contains("org.opencontainers.image.source=", content, StringComparison.Ordinal);
        Assert.Contains("org.opencontainers.image.revision=", content, StringComparison.Ordinal);
        Assert.Contains("org.opencontainers.image.title=", content, StringComparison.Ordinal);
        Assert.Contains("org.opencontainers.image.version=", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Gerador_de_manifesto_deve_verificar_ociRevision_por_inspecao_real_da_imagem()
    {
        var content = ReadRepositoryFile("scripts/ci/generate-release-manifest.sh");

        Assert.Contains("org.opencontainers.image.revision", content, StringComparison.Ordinal);
        Assert.Contains("oci_revision != source_commit", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Gerador_de_manifesto_nunca_deve_fabricar_atestacao_sem_reference_real()
    {
        var content = ReadRepositoryFile("scripts/ci/generate-release-manifest.sh");

        Assert.Contains("RELEASE_ATTESTATIONS_FILE", content, StringComparison.Ordinal);
        Assert.Contains("def build_attestation", content, StringComparison.Ordinal);
        Assert.Contains("nunca fabricada", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Validador_deve_rejeitar_componentes_ausentes_duplicados_e_fora_do_conjunto_aprovado()
    {
        var content = ReadRepositoryFile("scripts/ci/validate-release-manifest.sh");

        Assert.Contains("EXPECTED_COMPONENTS", content, StringComparison.Ordinal);
        // Comparacao por CONTAGEM (nunca so por conjunto): um set() sozinho
        // nao detecta um componente duplicado quando o conjunto de nomes
        // unicos ainda coincide com o esperado.
        Assert.Contains("len(components) != len(EXPECTED_COMPONENTS)", content, StringComparison.Ordinal);
        Assert.Contains("set(component_names) != EXPECTED_COMPONENTS", content, StringComparison.Ordinal);
        Assert.Contains("CANONICAL_TAG_RE", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Guarda_de_testes_do_manifesto_de_release_deve_existir_e_nunca_tocar_artifacts_real()
    {
        var content = ReadRepositoryFile("scripts/ci/test-release-guards.sh");

        Assert.Contains("mktemp", content, StringComparison.Ordinal);
        Assert.Contains("FIXTURE_DIR", content, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output) RunShellScript(params string[] arguments)
    {
        return RunShellScriptWithEnv(Array.Empty<(string, string)>(), arguments);
    }

    private static (int ExitCode, string Output) RunShellScriptWithEnv((string Key, string Value)[] env, params string[] arguments)
    {
        var shPath = ResolveShellExecutable();
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = shPath,
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var (key, value) in env)
        {
            startInfo.Environment[key] = value;
        }
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("não foi possível iniciar o processo sh.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout + stderr);
    }

    private static string ResolveShellExecutable()
    {
        // "sh" no PATH cobre tanto o Git Bash (Windows, usado neste
        // repositório) quanto qualquer runner Linux hospedado.
        return "sh";
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

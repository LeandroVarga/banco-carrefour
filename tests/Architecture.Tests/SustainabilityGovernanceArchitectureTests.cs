using System.Linq;
using Xunit;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Governança da seção 4 (sustentabilidade local/CI): garante,
/// por leitura real dos arquivos (não por execução), que as correções de
/// vazamento de disco (LocalStack desalinhado, volume anônimo do Testcontainers,
/// rebuild redundante, cache Terraform duplicado, remoção sem revalidação,
/// reexecução redundante da prova OCI) permanecem em vigor e não regridem
/// silenciosamente em uma alteração futura.
/// </summary>
public sealed class SustainabilityGovernanceArchitectureTests
{
    private const string LocalStackImageTag = "localstack/localstack:4.14.0";
    private const string PostgresTmpfsPath = "/var/lib/postgresql/data";

    private static readonly string[] FixturesWithLocalStack =
    [
        "tests/Consolidation.IntegrationTests/ConsolidationIntegrationCollection.cs",
        "tests/Security.IntegrationTests/Edge/AuthenticatedEdgeFlowFixture.cs",
        "tests/Security.IntegrationTests/LocalStackSecurity/LocalStackSecurityFixture.cs"
    ];

    private static readonly string[] FixturesWithPostgresTestcontainers =
    [
        "tests/Consolidation.IntegrationTests/ConsolidationIntegrationCollection.cs",
        "tests/Security.IntegrationTests/Edge/AuthenticatedEdgeFlowFixture.cs",
        "tests/Ledger.IntegrationTests/LedgerIntegrationCollection.cs",
        "tests/Security.IntegrationTests/Identity/IdentityFixture.cs",
        "tests/Security.IntegrationTests/Database/DatabasePrivilegesFixture.cs"
    ];

    [Fact]
    public void Todas_as_fixtures_de_LocalStack_devem_usar_a_mesma_tag_fixada()
    {
        foreach (var relativePath in FixturesWithLocalStack)
        {
            var content = ReadRepositoryFile(relativePath);
            Assert.Contains(LocalStackImageTag, content, StringComparison.Ordinal);

            // Nenhuma fixture pode referenciar a tag "3" antiga (deriva de versão).
            Assert.DoesNotContain("localstack/localstack:3\"", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Todas_as_fixtures_PostgreSQL_do_Testcontainers_devem_usar_WithTmpfsMount()
    {
        // WithTmpfsMount evita o volume anonimo criado pelo VOLUME declarado na
        // propria imagem postgres:16-alpine (Config.Volumes) - sem isto, cada
        // execucao de teste de integracao acumula um volume orfao mesmo com
        // WithCleanUp(true)/DisposeAsync (, item 4.2).
        foreach (var relativePath in FixturesWithPostgresTestcontainers)
        {
            var content = ReadRepositoryFile(relativePath);
            Assert.Contains($"WithTmpfsMount(\"{PostgresTmpfsPath}\")", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Script_de_deteccao_de_orfaos_do_Testcontainers_nao_deve_remover_por_padrao()
    {
        var content = ReadRepositoryFile("scripts/ci/detect-testcontainers-orphans.sh");

        Assert.DoesNotContain("docker system prune", content, StringComparison.Ordinal);
        Assert.DoesNotContain("docker volume prune", content, StringComparison.Ordinal);
        Assert.Contains("PROTECTED_NAMES", content, StringComparison.Ordinal);
        Assert.Contains("--remove", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_check_nonroot_images_antigo_nao_deve_mais_existir()
    {
        var scriptPath = Path.Combine(RepositoryRoot, "scripts", "ci", "check-nonroot-images.sh");
        Assert.False(File.Exists(scriptPath), "check-nonroot-images.sh deveria ter sido removido (substituído por verify-nonroot-from-manifest.sh, item 4.3).");
    }

    [Fact]
    public void Verificacao_nonroot_a_partir_do_manifesto_nao_deve_reconstruir_imagem()
    {
        var content = ReadRepositoryFile("scripts/ci/verify-nonroot-from-manifest.sh");
        Assert.DoesNotContain("docker build", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrapper_de_cache_compartilhado_do_Terraform_nao_deve_usar_TFE_TOKEN_nem_state_remoto()
    {
        var content = ReadRepositoryFile("scripts/ci/terraform-cached.sh");

        Assert.Contains("TF_PLUGIN_CACHE_DIR", content, StringComparison.Ordinal);
        Assert.DoesNotContain("TFE_TOKEN", content, StringComparison.Ordinal);
        Assert.DoesNotContain("HCP Terraform", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Diretorio_do_cache_compartilhado_do_Terraform_deve_estar_no_gitignore()
    {
        var gitignore = ReadRepositoryFile("infra/terraform/.gitignore");
        Assert.Contains(".terraform-plugin-cache", gitignore, StringComparison.Ordinal);
    }

    [Fact]
    public void Preflight_de_capacidade_nunca_deve_remover_nada_automaticamente()
    {
        var content = ReadRepositoryFile("scripts/ci/capacity-preflight.sh");

        Assert.DoesNotContain("docker rmi", content, StringComparison.Ordinal);
        Assert.DoesNotContain("docker volume rm", content, StringComparison.Ordinal);
        Assert.DoesNotContain("docker system prune", content, StringComparison.Ordinal);
        Assert.Contains("--fail-below-threshold", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Retencao_de_imagens_do_projeto_nunca_deve_usar_prune_ou_remocao_por_idade()
    {
        // Os comandos proibidos podem aparecer em PROSA no comentario de
        // cabecalho (documentando o que o script nunca faz) - a checagem real
        // precisa ignorar linhas de comentario e olhar so para codigo
        // executavel.
        var executableLines = ExecutableLines("scripts/ci/project-image-retention.sh");

        Assert.DoesNotContain("docker system prune", executableLines, StringComparison.Ordinal);
        Assert.DoesNotContain("docker image prune", executableLines, StringComparison.Ordinal);

        var content = ReadRepositoryFile("scripts/ci/project-image-retention.sh");
        Assert.Contains("is_full_sha", content, StringComparison.Ordinal);
        Assert.Contains("CURRENT_HEAD", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Retencao_de_imagens_nao_deve_usar_grep_F_com_ancora_literal_para_checar_candidatos()
    {
        // "grep -qF "^${target}|"" nunca teria casado com nada - o modo -F
        // (fixed-string) do GNU grep trata "^" como caractere LITERAL, nao
        // como ancora de inicio de linha (confirmado empiricamente: uma
        // remocao real de candidato genuino era sempre recusada). Corrigido
        // com comparacao exata campo-a-campo via leitura de linha.
        var executable = ExecutableLines("scripts/ci/project-image-retention.sh");

        Assert.DoesNotContain("grep -qF \"^${target}", executable, StringComparison.Ordinal);
        Assert.Contains("[ \"$candidate_ref\" = \"$target\" ]", executable, StringComparison.Ordinal);
    }

    [Fact]
    public void Retencao_de_imagens_deve_ler_o_loop_de_remocao_por_redirecionamento_e_nao_por_pipe()
    {
        // Um "while read" do lado direito de um pipe roda em subshell no POSIX
        // sh - contadores modificados dentro dele (REMOVED/REFUSED) seriam
        // perdidos ao sair do loop. O loop de remoção precisa ler de um
        // arquivo temporário via redirecionamento, nunca via pipe.
        var content = ReadRepositoryFile("scripts/ci/project-image-retention.sh");

        Assert.Contains("done < \"$REMOVE_REFS_FILE\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("echo \"$REMOVE_REFS\" | while", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Prova_generica_de_publicacao_OCI_deve_ter_gate_de_evidencia_por_HEAD_limpo()
    {
        var content = ReadRepositoryFile("scripts/ci/test-generic-oci-publish-proof.sh");

        Assert.Contains("SCRIPT_VERSION", content, StringComparison.Ordinal);
        Assert.Contains("EVIDENCE_FILE", content, StringComparison.Ordinal);
        Assert.Contains("CURRENT_COMMIT", content, StringComparison.Ordinal);
        Assert.Contains("IMAGE_MANIFEST_IDENTITY", content, StringComparison.Ordinal);
        Assert.Contains("TREE_CLEAN", content, StringComparison.Ordinal);
        Assert.Contains("--force", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Prova_OCI_nunca_deve_persistir_evidencia_com_arvore_suja()
    {
        var content = ReadRepositoryFile("scripts/ci/test-generic-oci-publish-proof.sh");

        Assert.Contains("if [ \"$TREE_CLEAN\" = \"true\" ]; then", content, StringComparison.Ordinal);
        Assert.Contains("evidencia NAO registrada", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_descartavel_da_prova_OCI_nao_deve_vazar_volume_anonimo()
    {
        // A imagem oficial "registry" declara /var/lib/registry como VOLUME
        // (mesma classe de bug corrigida em 4.2 para postgres:16-alpine) -
        // confirmado empiricamente que "docker rm -f" sozinho nao remove o
        // volume anonimo resultante, e que seria necessario --tmpfs com o
        // caminho EXATO "/var/lib/registry" (testado: o truque de barra
        // dupla usado para argumentos "-w"/"-e" faz o Docker registrar o
        // tmpfs numa chave diferente, sem sobrescrever o VOLUME da imagem -
        // MSYS_NO_PATHCONV=1 e a correcao correta para manter o valor exato
        // no Git Bash do Windows).
        var content = ReadRepositoryFile("scripts/ci/test-generic-oci-publish-proof.sh");

        Assert.Contains("MSYS_NO_PATHCONV=1", content, StringComparison.Ordinal);
        Assert.Contains("--tmpfs /var/lib/registry", content, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return File.ReadAllText(Path.Combine(RepositoryRoot, normalized));
    }

    /// <summary>
    /// Conteúdo de um script sh com as linhas de comentário (que começam com
    /// '#', ignorando espaço em branco à esquerda) removidas - usado para
    /// checagens de "comando proibido nunca é executado" sem ser enganado por
    /// prosa de documentação que cita o mesmo comando no cabeçalho.
    /// </summary>
    private static string ExecutableLines(string relativePath)
    {
        var lines = ReadRepositoryFile(relativePath).Split('\n');
        var executable = lines.Where(line => !line.TrimStart().StartsWith('#'));
        return string.Join('\n', executable);
    }

    private static readonly string[] FixturesThatCopyHostTerraformTree =
    [
        "tests/Security.IntegrationTests/Edge/AuthenticatedEdgeFlowFixture.cs",
        "tests/Security.IntegrationTests/LocalStackSecurity/LocalStackSecurityFixture.cs"
    ];

    [Fact]
    public void Fixtures_que_copiam_infra_terraform_do_host_devem_limpar_terraform_residual_antes_do_init()
    {
        // Achado real (fechamento pré-publicação, ): "cp -R"
        // copia symlinks COMO symlinks - se o host tiver usado
        // scripts/ci/terraform-cached.sh (cache compartilhado via
        // TF_PLUGIN_CACHE_DIR), ".terraform/providers/.../linux_amd64" no
        // host pode ser um symlink para um caminho absoluto que só existe
        // DENTRO de outro container - copiado para a fixture, vira um
        // symlink quebrado e "terraform init" recusa o pacote
        // ("Required plugins are not installed", reproduzido e corrigido
        // nesta auditoria: 65/105 testes de Security.IntegrationTests
        // falhavam antes da correção). Cada fixture que copia a árvore
        // infra/terraform do host precisa remover qualquer ".terraform"
        // residual da cópia ANTES do próprio "terraform init".
        foreach (var fixtureFile in FixturesThatCopyHostTerraformTree)
        {
            var content = ReadRepositoryFile(fixtureFile);

            Assert.Contains("cp -R /workspace/infra/terraform /tmp/terraform", content, StringComparison.Ordinal);
            Assert.Contains("rm -rf /tmp/terraform/environments/*/.terraform", content, StringComparison.Ordinal);
        }
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

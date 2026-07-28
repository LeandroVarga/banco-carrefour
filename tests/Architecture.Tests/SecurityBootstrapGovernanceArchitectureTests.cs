using Xunit;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Regressão dos defeitos reais encontrados na execução hospedada do
/// Performance smoke gate: ordem impossível de bootstrap de
/// <c>.env.security</c> num checkout limpo, e arquivos de segredo gerados
/// root:root num runner hospedado não-root (falha de permissão na limpeza).
/// </summary>
public sealed class SecurityBootstrapGovernanceArchitectureTests
{
    [Fact]
    public void Smoke_de_desempenho_nao_deve_exigir_env_security_antes_do_keycloak_bootstrap()
    {
        var content = ReadRepositoryFile("scripts/ci/run-performance-smoke.sh");

        var buildUpIndex = content.IndexOf("docker compose up -d --build", StringComparison.Ordinal);
        var keycloakBootstrapIndex = content.IndexOf("docker compose up keycloak-bootstrap", StringComparison.Ordinal);
        Assert.True(buildUpIndex >= 0, "run-performance-smoke.sh deve subir a stack real.");
        Assert.True(keycloakBootstrapIndex > buildUpIndex, "keycloak-bootstrap deve rodar depois da stack subir.");

        // A checagem de pré-condição de .env.security (se existir) nunca pode
        // vir ANTES do keycloak-bootstrap - isso tornaria a primeira execução
        // num checkout limpo impossível (achado real do CI hospedado).
        var premisePattern = "-f .local/security/.env.security ]";
        var firstEnvSecurityCheckIndex = content.IndexOf(premisePattern, StringComparison.Ordinal);
        if (firstEnvSecurityCheckIndex >= 0)
        {
            Assert.True(
                firstEnvSecurityCheckIndex > keycloakBootstrapIndex,
                "nenhuma checagem de .local/security/.env.security pode ocorrer antes de keycloak-bootstrap rodar.");
        }
    }

    [Fact]
    public void Smoke_de_desempenho_deve_verificar_env_security_depois_do_keycloak_bootstrap()
    {
        var content = ReadRepositoryFile("scripts/ci/run-performance-smoke.sh");

        var keycloakBootstrapIndex = content.IndexOf("docker compose up keycloak-bootstrap", StringComparison.Ordinal);
        var verificationIndex = content.IndexOf("-f .local/security/.env.security ]", StringComparison.Ordinal);

        Assert.True(keycloakBootstrapIndex >= 0);
        Assert.True(verificationIndex > keycloakBootstrapIndex, "run-performance-smoke.sh deve verificar .env.security DEPOIS de keycloak-bootstrap rodar.");

        var sourceIndex = content.IndexOf(". ./.local/security/.env.security", StringComparison.Ordinal);
        Assert.True(sourceIndex > verificationIndex, "a verificação de existência deve ocorrer antes de carregar .env.security.");
    }

    [Fact]
    public void Scripts_de_bootstrap_devem_preservar_modo_600_para_arquivos_de_segredo()
    {
        var implContent = ReadRepositoryFile("scripts/security/bootstrap-local-security-impl.sh");
        Assert.Contains("chmod 600", implContent, StringComparison.Ordinal);

        var keycloakContent = ReadRepositoryFile("scripts/security/keycloak-bootstrap.sh");
        Assert.True(
            keycloakContent.Contains("chmod 600", StringComparison.Ordinal) || keycloakContent.Contains("umask 077", StringComparison.Ordinal),
            "keycloak-bootstrap.sh deve restringir a permissão de .env.security a 600 (diretamente ou via umask 077).");
    }

    [Fact]
    public void Handoff_de_ownership_POSIX_deve_existir_sem_alterar_o_wrapper_Windows()
    {
        var wrapperContent = ReadRepositoryFile("scripts/security/bootstrap-local-security.sh");
        Assert.Contains("HOST_UID", wrapperContent, StringComparison.Ordinal);
        Assert.Contains("HOST_GID", wrapperContent, StringComparison.Ordinal);

        var implContent = ReadRepositoryFile("scripts/security/bootstrap-local-security-impl.sh");
        Assert.Contains("chown_to_host", implContent, StringComparison.Ordinal);

        var keycloakContent = ReadRepositoryFile("scripts/security/keycloak-bootstrap.sh");
        Assert.Contains("HOST_UID", keycloakContent, StringComparison.Ordinal);

        // O wrapper Windows nunca deve precisar conhecer HOST_UID/HOST_GID -
        // ACL do NTFS é um mecanismo totalmente diferente (Protect-FileAcl),
        // sempre aplicado ao usuário atual do processo PowerShell.
        var windowsWrapperContent = ReadRepositoryFile("scripts/security/bootstrap-local-security.ps1");
        Assert.DoesNotContain("HOST_UID", windowsWrapperContent, StringComparison.Ordinal);
        Assert.DoesNotContain("HOST_GID", windowsWrapperContent, StringComparison.Ordinal);
    }

    [Fact]
    public void EdgeKey_deve_manter_grupo_101_e_modo_640_depois_do_ownership_de_host()
    {
        var content = ReadRepositoryFile("scripts/security/bootstrap-local-security-impl.sh");

        var recursiveChownIndex = content.IndexOf("chown_to_host \"$CERTS_DIR\" 1", StringComparison.Ordinal);
        Assert.True(recursiveChownIndex >= 0, "o ownership de host recursivo do diretório de certificados deve existir.");

        var lastChgrpIndex = content.LastIndexOf("chgrp 101", StringComparison.Ordinal);
        var lastChmod640Index = content.LastIndexOf("chmod 640", StringComparison.Ordinal);
        Assert.True(lastChgrpIndex > recursiveChownIndex, "chgrp 101 em edge.key deve ser reaplicado DEPOIS do chown recursivo de host.");
        Assert.True(lastChmod640Index > recursiveChownIndex, "chmod 640 em edge.key deve ser reaplicado DEPOIS do chown recursivo de host.");
    }

    [Fact]
    public void Cleanup_do_smoke_de_desempenho_deve_permanecer_escopado()
    {
        var content = ReadRepositoryFile("scripts/ci/run-performance-smoke.sh");

        Assert.Contains("docker compose down", content, StringComparison.Ordinal);
        Assert.DoesNotContain("docker compose down -v", content, StringComparison.Ordinal);
        Assert.DoesNotContain("docker compose down --volumes", content, StringComparison.Ordinal);
        Assert.DoesNotContain("docker system prune", content, StringComparison.Ordinal);
        Assert.DoesNotContain("docker container prune", content, StringComparison.Ordinal);
        Assert.DoesNotContain("docker volume prune", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Env_e_env_security_devem_permanecer_ignorados_pelo_git()
    {
        var gitignore = ReadRepositoryFile(".gitignore");

        Assert.Contains("/.env", gitignore, StringComparison.Ordinal);
        Assert.Contains(".local/security/", gitignore, StringComparison.Ordinal);
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

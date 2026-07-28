using Xunit;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Regras de regressão da migração RS256/Keycloak: nenhuma
/// forma de HS256/chave simétrica ou de HTTPS opcional pode voltar ao
/// código de produção, e os artefatos de teste adversariais (emissor JWT
/// de ataque, chave RSA efêmera) ficam restritos a <c>tests/</c>.
/// </summary>
public sealed class SecurityRegressionArchitectureTests
{
    [Fact]
    public void Codigo_de_producao_nao_deve_conter_HS256_ou_chave_simetrica()
    {
        var productionSource = ReadAllSources("src");

        Assert.DoesNotContain("SymmetricSecurityKey", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HmacSha256", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SecurityAlgorithms.HmacSha256", productionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Codigo_de_producao_nao_deve_desabilitar_RequireHttpsMetadata()
    {
        var productionSource = ReadAllSources("src");

        Assert.DoesNotContain("RequireHttpsMetadata = false", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireHttpsMetadata=false", productionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Emissor_JWT_adversarial_e_chave_RSA_de_teste_devem_ficar_restritos_a_tests()
    {
        var repositoryRoot = RepositoryRoot;

        var adversarialIssuerMatches = Directory
            .EnumerateFiles(repositoryRoot, "AdversarialJwtIssuer.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .Where(path => !IsBuildOutput(path))
            .ToArray();

        Assert.NotEmpty(adversarialIssuerMatches);
        Assert.All(adversarialIssuerMatches, path =>
            Assert.StartsWith($"tests{Path.DirectorySeparatorChar}", path, StringComparison.Ordinal));

        var productionSource = ReadAllSources("src");
        Assert.DoesNotContain("AdversarialJwtIssuer", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EphemeralCertificateAuthority", productionSource, StringComparison.Ordinal);
    }

    private static string ReadAllSources(params string[] segments)
    {
        var directory = Path.Combine(RepositoryRoot, Path.Combine(segments));

        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(Path.GetRelativePath(RepositoryRoot, path)))
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static bool IsBuildOutput(string relativePath)
    {
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("bin", StringComparer.Ordinal) || segments.Contains("obj", StringComparer.Ordinal);
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

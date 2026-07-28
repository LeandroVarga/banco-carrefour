using Xunit;

namespace BancoCarrefour.Architecture.Tests;

public sealed class TestInfrastructureArchitectureTests
{
    [Fact]
    public void Testcontainers_deve_ficar_restrito_a_projetos_de_teste()
    {
        var packageReferences = Directory
            .EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(Path.GetRelativePath(RepositoryRoot, path)))
            .Select(path => new
            {
                Path = Path.GetRelativePath(RepositoryRoot, path),
                Content = File.ReadAllText(path)
            })
            .Where(project => project.Content.Contains("Testcontainers", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(packageReferences);
        Assert.All(packageReferences, project =>
            Assert.StartsWith($"tests{Path.DirectorySeparatorChar}", project.Path));
    }

    [Fact]
    public void Codigo_de_producao_nao_deve_conter_logica_especifica_de_Testcontainers()
    {
        var productionSource = ReadAllSources("src");

        Assert.DoesNotContain("Testcontainers", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DotNet.Testcontainers", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TESTCONTAINERS", productionSource, StringComparison.OrdinalIgnoreCase);
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

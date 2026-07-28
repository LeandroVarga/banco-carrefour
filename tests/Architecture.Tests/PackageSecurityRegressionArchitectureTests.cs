using System.Xml.Linq;
using Xunit;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Regressão de dependências NuGet vulneráveis conhecidas: protege contra
/// um retrocesso trivial do literal de versão central que reintroduziria
/// uma CVE já corrigida.
/// </summary>
public sealed class PackageSecurityRegressionArchitectureTests
{
    [Fact]
    public void Microsoft_Extensions_Hosting_nao_deve_regredir_para_a_versao_que_carrega_System_Text_Json_vulneravel()
    {
        // Achado real da auditoria NuGet hospedada: Microsoft.Extensions.Hosting
        // 8.0.0 (unica outra versao 8.0.x publicada, junto com 8.0.1) arrasta
        // System.Text.Json 8.0.0 (GHSA-hh2w-p6rv-4g7w e GHSA-8g4q-xg66-9fp4)
        // via Configuration.Json/Logging.Console/Logging.EventSource. A
        // correcao (8.0.1) remove System.Text.Json inteiramente do grafo
        // transitivo explicito (passa a ser resolvido pelo framework),
        // conforme comprovado por "dotnet list package --vulnerable" e pelo
        // proprio scripts/ci/nuget-dependency-audit.sh (gate real de CI).
        // Este teste protege apenas contra um retrocesso trivial do literal
        // de versao central - a prova executavel completa e o gate de CI.
        var doc = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Packages.props"));
        var hostingVersion = doc.Descendants("PackageVersion")
            .Single(e => e.Attribute("Include")?.Value == "Microsoft.Extensions.Hosting")
            .Attribute("Version")!.Value;

        Assert.NotEqual("8.0.0", hostingVersion);
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

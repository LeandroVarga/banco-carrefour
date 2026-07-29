using BancoCarrefour.Consolidation.Application.ApplyFinancialEntry;
using BancoCarrefour.Consolidation.Application.GetDailyBalance;
using BancoCarrefour.Consolidation.Infrastructure.DailyBalances;
using Xunit;

namespace BancoCarrefour.Architecture.Tests;

public sealed class ConsolidationArchitectureTests
{
    [Fact]
    public void Application_nao_deve_conter_JSON_AWS_ASPNET_EF_ou_Npgsql()
    {
        var applicationSource = ReadAllSources("src", "Consolidation", "Consolidation.Application");

        Assert.DoesNotContain("System.Text.Json", applicationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", applicationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Amazon", applicationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SQS", applicationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpContext", applicationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ClaimsPrincipal", applicationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EntityFrameworkCore", applicationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", applicationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", applicationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_nao_deve_executar_SQL_ou_conter_regra_financeira()
    {
        var workerSource = ReadAllSources("src", "Consolidation", "Consolidation.Worker");

        Assert.DoesNotContain("ExecuteSql", workerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FromSql", workerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT ", workerSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT ", workerSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", workerSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DailyBalanceContribution", workerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Money.", workerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Endpoint_de_dailyBalance_nao_deve_receber_ConsolidationDbContext()
    {
        var endpointSource = ReadSource("src", "Consolidation", "Consolidation.Api", "DailyBalances", "DailyBalanceEndpoints.cs");

        Assert.DoesNotContain("ConsolidationDbContext", endpointSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Infrastructure_deve_implementar_portas_do_Consolidation()
    {
        Assert.True(typeof(IDailyBalanceProjectionStore).IsAssignableFrom(typeof(EfDailyBalanceProjectionStore)));
        Assert.True(typeof(IDailyBalanceReader).IsAssignableFrom(typeof(EfDailyBalanceReader)));
    }

    private static string ReadSource(params string[] segments)
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot, Path.Combine(segments)));
    }

    private static string ReadAllSources(params string[] segments)
    {
        var directory = Path.Combine(RepositoryRoot, Path.Combine(segments));

        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
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

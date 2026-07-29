using BancoCarrefour.Ledger.Application.PublishOutbox;
using BancoCarrefour.Ledger.Infrastructure.Outbox;
using Xunit;

namespace BancoCarrefour.Architecture.Tests;

public sealed class LedgerPublisherArchitectureTests
{
    [Fact]
    public void OutboxPublisher_host_nao_deve_acessar_DbContext_SQL_ou_SQS_diretamente()
    {
        var programSource = ReadSource("src", "Ledger", "Ledger.OutboxPublisher", "Program.cs");
        var workerSource = ReadSource("src", "Ledger", "Ledger.OutboxPublisher", "Worker.cs");
        var hostSource = programSource + Environment.NewLine + workerSource;

        Assert.DoesNotContain("LedgerDbContext", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FromSql", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SendMessageAsync", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OutboxMessageStatus", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Caso_de_uso_de_publicacao_nao_deve_conhecer_EF_PostgreSQL_ou_SQS()
    {
        var source = ReadSource("src", "Ledger", "Ledger.Application", "PublishOutbox", "PublishPendingEventsUseCase.cs");

        Assert.DoesNotContain("EntityFrameworkCore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Amazon", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SQS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Infrastructure_deve_implementar_portas_da_publicacao()
    {
        Assert.True(typeof(IOutboxStore).IsAssignableFrom(typeof(PostgresOutboxStore)));
        Assert.True(typeof(IIntegrationEventPublisher).IsAssignableFrom(typeof(SqsIntegrationEventPublisher)));
    }

    private static string ReadSource(params string[] segments)
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot, Path.Combine(segments)));
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

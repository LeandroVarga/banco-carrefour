using System.Reflection;
using BancoCarrefour.Contracts;
using BancoCarrefour.Ledger.Api;
using BancoCarrefour.Ledger.Application;
using BancoCarrefour.Ledger.Domain;
using BancoCarrefour.Ledger.Infrastructure;
using BancoCarrefour.Ledger.OutboxPublisher;
using Xunit;
using ConsolidationApplicationMarker = BancoCarrefour.Consolidation.Application.ApplicationAssemblyMarker;
using ConsolidationDomainMarker = BancoCarrefour.Consolidation.Domain.DomainAssemblyMarker;
using ConsolidationInfrastructureMarker = BancoCarrefour.Consolidation.Infrastructure.InfrastructureAssemblyMarker;

namespace BancoCarrefour.Architecture.Tests;

public sealed class LedgerArchitectureTests
{
    [Fact]
    public void Domain_nao_deve_referenciar_camadas_ou_frameworks()
    {
        var references = ReferencedAssemblyNames(typeof(DomainAssemblyMarker).Assembly);

        Assert.DoesNotContain(typeof(ApplicationAssemblyMarker).Assembly.GetName().Name, references);
        Assert.DoesNotContain(typeof(InfrastructureAssemblyMarker).Assembly.GetName().Name, references);
        Assert.DoesNotContain(typeof(ApiAssemblyMarker).Assembly.GetName().Name, references);
        Assert.DoesNotContain(typeof(OutboxPublisherAssemblyMarker).Assembly.GetName().Name, references);
        Assert.DoesNotContain("BancoCarrefour.Contracts", references);
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Npgsql", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("RabbitMQ", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        // ADR-0009: AWS SDK (Secrets Manager/SQS) fica restrito à
        // Infrastructure/composition root - nunca no Domain.
        Assert.DoesNotContain(references, name => name.StartsWith("Amazon", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_deve_depender_somente_do_domain_no_Ledger()
    {
        var references = ReferencedAssemblyNames(typeof(ApplicationAssemblyMarker).Assembly);

        Assert.Contains(typeof(DomainAssemblyMarker).Assembly.GetName().Name, references);
        Assert.DoesNotContain(typeof(InfrastructureAssemblyMarker).Assembly.GetName().Name, references);
        Assert.DoesNotContain(typeof(ApiAssemblyMarker).Assembly.GetName().Name, references);
        Assert.DoesNotContain(typeof(OutboxPublisherAssemblyMarker).Assembly.GetName().Name, references);
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Npgsql", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("RabbitMQ", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        // ADR-0009: AWS SDK (Secrets Manager/SQS) fica restrito à
        // Infrastructure/composition root - nunca na Application.
        Assert.DoesNotContain(references, name => name.StartsWith("Amazon", StringComparison.Ordinal));
    }

    [Fact]
    public void Contracts_nao_deve_depender_do_domain()
    {
        var references = ReferencedAssemblyNames(typeof(ContractAssemblyMarker).Assembly);

        Assert.DoesNotContain(references, name => name.Contains(".Domain", StringComparison.Ordinal));
    }

    [Fact]
    public void Consolidation_Domain_nao_deve_referenciar_camadas_ou_frameworks()
    {
        var references = ReferencedAssemblyNames(typeof(ConsolidationDomainMarker).Assembly);

        Assert.DoesNotContain(typeof(ConsolidationApplicationMarker).Assembly.GetName().Name, references);
        Assert.DoesNotContain(typeof(ConsolidationInfrastructureMarker).Assembly.GetName().Name, references);
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Npgsql", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Amazon", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Consolidation_Application_deve_depender_somente_do_domain_no_Consolidation()
    {
        var references = ReferencedAssemblyNames(typeof(ConsolidationApplicationMarker).Assembly);

        Assert.Contains(typeof(ConsolidationDomainMarker).Assembly.GetName().Name, references);
        Assert.DoesNotContain(typeof(ConsolidationInfrastructureMarker).Assembly.GetName().Name, references);
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Npgsql", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Amazon", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Ledger_e_Consolidation_nao_devem_referenciar_um_ao_outro()
    {
        foreach (var assembly in LedgerAssemblies())
        {
            Assert.DoesNotContain(ReferencedAssemblyNames(assembly), name => name.StartsWith("BancoCarrefour.Consolidation", StringComparison.Ordinal));
        }

        foreach (var assembly in ConsolidationAssemblies())
        {
            Assert.DoesNotContain(ReferencedAssemblyNames(assembly), name => name.StartsWith("BancoCarrefour.Ledger", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Endpoint_de_entries_nao_deve_conter_LedgerDbContext()
    {
        var endpointSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Ledger", "Ledger.Api", "Entries", "EntryEndpoints.cs"));

        Assert.DoesNotContain("LedgerDbContext", endpointSource, StringComparison.Ordinal);
    }

    [Fact]
    public void LedgerDbContext_deve_ser_tipo_da_infrastructure()
    {
        Assert.Equal("BancoCarrefour.Ledger.Infrastructure", typeof(LedgerDbContext).Namespace);
    }

    private static IReadOnlyCollection<string> ReferencedAssemblyNames(Assembly assembly)
    {
        return assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();
    }

    private static Assembly[] LedgerAssemblies()
    {
        return
        [
            typeof(DomainAssemblyMarker).Assembly,
            typeof(ApplicationAssemblyMarker).Assembly,
            typeof(InfrastructureAssemblyMarker).Assembly,
            typeof(ApiAssemblyMarker).Assembly,
            typeof(OutboxPublisherAssemblyMarker).Assembly
        ];
    }

    private static Assembly[] ConsolidationAssemblies()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name?.StartsWith("BancoCarrefour.Consolidation", StringComparison.Ordinal) == true)
            .ToArray();
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

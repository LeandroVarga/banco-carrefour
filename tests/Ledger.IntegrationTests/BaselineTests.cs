using BancoCarrefour.Ledger.Api;
using BancoCarrefour.Ledger.OutboxPublisher;
using BancoCarrefour.Ledger.Persistence;
using Xunit;

namespace BancoCarrefour.Ledger.IntegrationTests;

public sealed class BaselineTests
{
    [Fact]
    public void Deve_carregar_assemblies_do_ledger()
    {
        Assert.Equal("Ledger.Api", typeof(ApiAssemblyMarker).Assembly.GetName().Name);
        Assert.Equal("Ledger.Persistence", typeof(PersistenceAssemblyMarker).Assembly.GetName().Name);
        Assert.Equal("Ledger.OutboxPublisher", typeof(OutboxPublisherAssemblyMarker).Assembly.GetName().Name);
    }
}

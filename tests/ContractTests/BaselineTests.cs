using BancoCarrefour.Contracts;
using Xunit;

namespace ContractTests;

public sealed class BaselineTests
{
    [Fact]
    public void Deve_carregar_assembly_de_contratos()
    {
        var assembly = typeof(ContractAssemblyMarker).Assembly;

        Assert.Equal("BancoCarrefour.Contracts", assembly.GetName().Name);
    }
}

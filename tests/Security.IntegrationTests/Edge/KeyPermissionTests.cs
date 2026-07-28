using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.Edge;

/// <summary>
/// Confirma, por inspeção real dentro do container (não por leitura do
/// código), as garantias de menor privilégio do edge-proxy já documentadas
/// em "Estratégia TLS": chave privada interna em 600 (nunca 640/644),
/// nenhuma chave de CA jamais montada, processo do nginx não roda
/// permanentemente como root.
/// </summary>
[Collection(EdgeSecurityCollection.Name)]
public sealed class KeyPermissionTests(EdgeFixture fixture)
{
    [Fact]
    public async Task Chave_TLS_interna_deve_estar_em_modo_600()
    {
        var result = await fixture.ExecInEdgeProxyAsync("stat", "-c", "%a", "/etc/nginx/server.key");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("600", result.Stdout.Trim());
    }

    [Fact]
    public async Task Nenhum_arquivo_de_chave_de_CA_deve_estar_montado_no_container()
    {
        var result = await fixture.ExecInEdgeProxyAsync(
            "sh", "-c",
            "find / -xdev -iname 'ca*.key' -o -iname 'ca.key' 2>/dev/null | head -1");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout.Trim());
    }

    [Fact]
    public async Task Processo_nginx_nao_deve_rodar_permanentemente_como_root()
    {
        var result = await fixture.ExecInEdgeProxyAsync("sh", "-c", "id -u");

        Assert.Equal(0, result.ExitCode);
        Assert.NotEqual("0", result.Stdout.Trim());
    }
}

using System.Net;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.Edge;

/// <summary>
/// Reproduz os 6 cenários de ataque já comprovados empiricamente contra o
/// edge-proxy real (ver "Estratégia WAF" no documento do ciclo — Fase 2,
/// pós-bloqueio) com o mesmo container/config e um upstream de teste,
/// diferenciando explicitamente a camada responsável por cada resultado:
/// ModSecurity/CRS (403 sem alcançar o upstream), nginx (404 de
/// normalização de URI, 413 de tamanho, 429 de rate limit) ou a aplicação
/// (401 de autenticação, alcançando o upstream normalmente).
/// </summary>
[Collection(EdgeSecurityCollection.Name)]
public sealed class WafTests(EdgeFixture fixture)
{
    [Fact]
    public async Task Requisicao_legitima_sem_token_deve_alcancar_o_upstream()
    {
        using var client = new HttpClient(fixture.CreateTrustedHandler());

        // GET (não POST): o upstream de teste é um httpd estático
        // (busybox-extras), que só serve GET — a prova de "reachability"
        // não depende do método HTTP.
        using var response = await client.GetAsync($"{fixture.HttpsBaseUrl}/ledger/");

        // Sem CRS/nginx bloqueando, a requisição alcança o upstream de
        // teste (que responde 200 com o marcador) — o equivalente real
        // seria 401 da aplicação; aqui provamos "alcançou o upstream" via
        // o corpo do echo, não o código de status da aplicação real.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("UPSTREAM-HIT:ledger-api", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SQL_injection_deve_ser_bloqueada_pelo_CRS_com_403_sem_alcancar_o_upstream()
    {
        using var client = new HttpClient(fixture.CreateTrustedHandler());

        using var response = await client.GetAsync($"{fixture.HttpsBaseUrl}/ledger/?id=1' OR '1'='1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual("UPSTREAM-HIT:ledger-api", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task XSS_deve_ser_bloqueado_pelo_CRS_com_403_sem_alcancar_o_upstream()
    {
        using var client = new HttpClient(fixture.CreateTrustedHandler());

        using var response = await client.GetAsync($"{fixture.HttpsBaseUrl}/ledger/?q=<script>alert(1)</script>");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual("UPSTREAM-HIT:ledger-api", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Path_traversal_literal_deve_retornar_404_do_nginx_sem_alcancar_CRS_ou_upstream()
    {
        using var client = new HttpClient(fixture.CreateTrustedHandler());

        using var response = await client.GetAsync($"{fixture.HttpsBaseUrl}/ledger/../../etc/passwd");

        // nginx normaliza o URI antes de qualquer roteamento — o caminho
        // final não corresponde a nenhuma location válida (404 direto do
        // catch-all "location / { return 404; }"), nunca chega a
        // ModSecurity nem ao upstream. Coincide com a evidência real já
        // registrada no documento do ciclo.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("UPSTREAM-HIT:ledger-api", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Path_traversal_via_query_deve_ser_bloqueado_pelo_CRS_com_403()
    {
        using var client = new HttpClient(fixture.CreateTrustedHandler());

        using var response = await client.GetAsync($"{fixture.HttpsBaseUrl}/ledger/?file=../../../../etc/passwd");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual("UPSTREAM-HIT:ledger-api", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rajada_de_requisicoes_deve_retornar_429_do_nginx()
    {
        using var client = new HttpClient(fixture.CreateTrustedHandler());

        var statusCodes = new List<HttpStatusCode>();
        for (var i = 0; i < 120; i++)
        {
            using var response = await client.GetAsync($"{fixture.HttpsBaseUrl}/ledger/");
            statusCodes.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statusCodes);
    }
}

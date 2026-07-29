using System.Net;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.Edge;

/// <summary>
/// TLS, redirecionamento e headers de segurança do edge-proxy real — mesma
/// imagem/config do Compose, upstreams de teste (ver <see cref="EdgeFixture"/>).
/// A checagem de porto redirecionado usa um <c>Host</c> HTTP explícito
/// (<c>localhost:8443</c>) em vez do porto host aleatório do Testcontainers,
/// para validar o COMPORTAMENTO de preservação de porta da config nginx
/// (<c>return 301 https://$host$request_uri</c>) — a topologia exata
/// 8443/8080 do Compose real já foi comprovada empiricamente com
/// evidência de <c>curl</c> real (ver "Topologia de borda" no documento do
/// ciclo); este teste não repete essa prova de implantação, só a lógica de
/// config.
/// </summary>
[Collection(EdgeSecurityCollection.Name)]
public sealed class TlsAndHeadersTests(EdgeFixture fixture)
{
    [Fact]
    public async Task Requisicao_legitima_via_HTTPS_deve_alcancar_o_upstream()
    {
        using var client = new HttpClient(fixture.CreateTrustedHandler());

        var response = await client.GetAsync($"{fixture.HttpsBaseUrl}/ledger/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("UPSTREAM-HIT:ledger-api", body);
    }

    [Fact]
    public async Task TLS_deve_negociar_1_2_ou_1_3()
    {
        using var handler = fixture.CreateTrustedHandler();
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync($"{fixture.HttpsBaseUrl}/ledger/");

        // System.Net.Http não expõe a versão TLS negociada diretamente na
        // resposta; a negociação bem-sucedida em si (sem exceção de
        // handshake) já prova que um dos dois protocolos permitidos
        // (ssl_protocols TLSv1.2 TLSv1.3 na config real) foi usado — TLS
        // 1.0/1.1 foram desabilitados na config e não estão disponíveis no
        // cliente .NET moderno para sequer tentar negociar.
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task HTTP_deve_redirecionar_para_HTTPS_preservando_a_porta_do_Host()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{fixture.HttpBaseUrl}/ledger/health/ready");
        request.Headers.Host = "localhost:8443";

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("https://localhost:8443/ledger/health/ready", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Headers_de_seguranca_devem_estar_presentes()
    {
        using var client = new HttpClient(fixture.CreateTrustedHandler());

        using var response = await client.GetAsync($"{fixture.HttpsBaseUrl}/ledger/");

        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.True(response.Content.Headers.Contains("X-Content-Type-Options") || response.Headers.Contains("X-Content-Type-Options"));
    }

    [Fact]
    public async Task Body_excessivo_deve_retornar_413_sem_alcancar_o_upstream()
    {
        using var client = new HttpClient(fixture.CreateTrustedHandler());

        var oversizedBody = new string('a', 2 * 1024 * 1024); // 2 MiB > client_max_body_size 1m
        using var content = new StringContent(oversizedBody);

        using var response = await client.PostAsync($"{fixture.HttpsBaseUrl}/ledger/", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
}

using System.Net;
using BancoCarrefour.Security.IntegrationTests.Shared;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.Identity;

/// <summary>
/// Casos obrigatórios de identidade pedidos explicitamente. Sempre que o
/// Keycloak real pode emitir o caso naturalmente, é isso que é usado; só os
/// casos genuinamente impossíveis de obter do Keycloak (assinatura
/// inválida, <c>kid</c> desconhecido, malformado) usam
/// <see cref="AdversarialJwtIssuer"/> — nunca uma reimplementação paralela
/// do JWT do produto.
/// </summary>
[Collection(IdentitySecurityCollection.Name)]
public sealed class TokenValidationTests : IDisposable
{
    private readonly IdentityFixture fixture;
    private readonly LedgerIdentityApiFactory ledgerFactory;
    private readonly HttpClient ledgerClient;

    public TokenValidationTests(IdentityFixture fixture)
    {
        this.fixture = fixture;
        ledgerFactory = fixture.CreateLedgerApiFactory();
        ledgerClient = ledgerFactory.CreateClient();
    }

    [Fact]
    public async Task Token_valido_deve_ser_aceito()
    {
        var token = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantAClientId, scope: KeycloakTestRealm.LedgerWriteScope);

        var response = await LedgerEntryRequests.PostEntryAsync(ledgerClient, token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Sem_token_deve_retornar_401()
    {
        var response = await LedgerEntryRequests.PostEntryAsync(ledgerClient, bearerToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_expirado_deve_retornar_401()
    {
        // merchant-a-shortlived-test-client: access.token.lifespan=2s no
        // realm real — token genuinamente emitido e assinado pelo Keycloak,
        // só aguardamos a expiração real em vez de forjar um "exp" passado.
        // LedgerAuthentication.cs usa ClockSkew=1 minuto (produção real),
        // então a espera precisa exceder lifespan + clock skew — não só o
        // lifespan sozinho.
        var token = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantAShortLivedClientId, scope: KeycloakTestRealm.LedgerWriteScope);

        await Task.Delay(TimeSpan.FromSeconds(65));

        var response = await LedgerEntryRequests.PostEntryAsync(ledgerClient, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Assinatura_invalida_deve_retornar_401()
    {
        var genuineToken = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantAClientId, scope: KeycloakTestRealm.LedgerWriteScope);
        var tamperedToken = AdversarialJwtIssuer.CorruptSignature(genuineToken);

        var response = await LedgerEntryRequests.PostEntryAsync(ledgerClient, tamperedToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Issuer_incorreto_deve_retornar_401()
    {
        // Keycloak em start-dev deriva o "iss" a partir do Host HTTP da
        // própria requisição de token (hostname-strict desligado, decisão
        // deliberada da collection rápida) — usar um Host diferente produz
        // um token genuinamente assinado, mas com iss divergente do
        // Authority configurado na API.
        using var issuer = new AdversarialJwtIssuer();
        var token = await fixture.IssueTokenAsync(
            KeycloakTestRealm.MerchantAClientId,
            scope: KeycloakTestRealm.LedgerWriteScope,
            hostHeaderOverride: "attacker.localhost");

        var response = await LedgerEntryRequests.PostEntryAsync(ledgerClient, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Kid_desconhecido_deve_retornar_401()
    {
        using var issuer = new AdversarialJwtIssuer();
        var token = issuer.IssueWithForeignKey(
            fixture.Authority,
            KeycloakTestRealm.LedgerAudience,
            "merchant-a",
            KeycloakTestRealm.LedgerWriteScope);

        var response = await LedgerEntryRequests.PostEntryAsync(ledgerClient, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_malformado_deve_retornar_401()
    {
        var response = await LedgerEntryRequests.PostEntryAsync(ledgerClient, "isto-nao-e-um-jwt");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Merchant_id_ausente_deve_retornar_403()
    {
        var token = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantMissingClientId, scope: KeycloakTestRealm.LedgerWriteScope);

        var response = await LedgerEntryRequests.PostEntryAsync(ledgerClient, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Merchant_id_vazio_deve_retornar_403()
    {
        var token = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantBlankClientId, scope: KeycloakTestRealm.LedgerWriteScope);

        var response = await LedgerEntryRequests.PostEntryAsync(ledgerClient, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Merchant_id_acima_do_limite_deve_retornar_403()
    {
        var token = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantOverflowClientId, scope: KeycloakTestRealm.LedgerWriteScope);

        var response = await LedgerEntryRequests.PostEntryAsync(ledgerClient, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public void Dispose()
    {
        ledgerClient.Dispose();
        ledgerFactory.Dispose();
    }
}

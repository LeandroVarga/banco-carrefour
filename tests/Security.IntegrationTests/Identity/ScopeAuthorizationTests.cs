using System.Net;
using BancoCarrefour.Security.IntegrationTests.Shared;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.Identity;

/// <summary>
/// Cobre os 7 cenários de scope/audience pedidos explicitamente para a
/// defesa em profundidade da policy de scope. Como o audience mapper vive
/// dentro do próprio client scope (ledger.write/consolidation.read — ver
/// ADR-0007), a maioria das combinações naturais acopla audience e scope;
/// os casos 4-6 usam <see cref="KeycloakTestRealm.MerchantAAudienceOnlyClientId"/>
/// (client de teste com audience atribuída no nível do client, sem nenhum
/// optional scope) especificamente para desacoplar as duas dimensões e
/// provar que a policy de scope rejeita mesmo com audience correta — sem
/// isso, esses 3 casos não seriam observáveis com os clients de produção.
/// </summary>
[Collection(IdentitySecurityCollection.Name)]
public sealed class ScopeAuthorizationTests : IDisposable
{
    private readonly IdentityFixture fixture;
    private readonly LedgerIdentityApiFactory ledgerFactory;
    private readonly ConsolidationIdentityApiFactory consolidationFactory;
    private readonly HttpClient ledgerClient;
    private readonly HttpClient consolidationClient;

    public ScopeAuthorizationTests(IdentityFixture fixture)
    {
        this.fixture = fixture;
        ledgerFactory = fixture.CreateLedgerApiFactory();
        consolidationFactory = fixture.CreateConsolidationApiFactory();
        ledgerClient = ledgerFactory.CreateClient();
        consolidationClient = consolidationFactory.CreateClient();
    }

    [Fact]
    public async Task Caso1_ledger_write_permite_Ledger_e_nega_Consolidation()
    {
        var token = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantAClientId, scope: KeycloakTestRealm.LedgerWriteScope);

        var ledgerResponse = await LedgerEntryRequests.PostEntryAsync(ledgerClient, token);
        Assert.Equal(HttpStatusCode.Created, ledgerResponse.StatusCode);

        var consolidationResponse = await LedgerEntryRequests.GetDailyBalanceAsync(consolidationClient, token);
        Assert.Equal(HttpStatusCode.Unauthorized, consolidationResponse.StatusCode);
    }

    [Fact]
    public async Task Caso2_consolidation_read_permite_Consolidation_e_nega_Ledger()
    {
        var token = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantAClientId, scope: KeycloakTestRealm.ConsolidationReadScope);

        var consolidationResponse = await LedgerEntryRequests.GetDailyBalanceAsync(consolidationClient, token);
        Assert.True(
            consolidationResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound,
            $"Esperado 200 ou 404 (autorizado), obtido {consolidationResponse.StatusCode}");

        var ledgerResponse = await LedgerEntryRequests.PostEntryAsync(ledgerClient, token);
        Assert.Equal(HttpStatusCode.Unauthorized, ledgerResponse.StatusCode);
    }

    [Fact]
    public async Task Caso3_ambos_scopes_permitem_as_duas_APIs()
    {
        var token = await fixture.IssueTokenAsync(
            KeycloakTestRealm.MerchantAClientId,
            scope: $"{KeycloakTestRealm.LedgerWriteScope} {KeycloakTestRealm.ConsolidationReadScope}");

        var ledgerResponse = await LedgerEntryRequests.PostEntryAsync(ledgerClient, token);
        Assert.Equal(HttpStatusCode.Created, ledgerResponse.StatusCode);

        var consolidationResponse = await LedgerEntryRequests.GetDailyBalanceAsync(consolidationClient, token);
        Assert.True(
            consolidationResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound,
            $"Esperado 200 ou 404 (autorizado), obtido {consolidationResponse.StatusCode}");
    }

    [Fact]
    public async Task Caso4_scope_ausente_com_audience_correta_resulta_em_403()
    {
        var token = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantAAudienceOnlyClientId);

        var response = await LedgerEntryRequests.PostEntryAsync(ledgerClient, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Caso5_scope_desconhecido_e_rejeitado_pelo_proprio_Keycloak_com_400()
    {
        // Comportamento observado por execução real: o Keycloak NÃO
        // descarta silenciosamente um scope solicitado que não está
        // atribuído (default nem optional) ao client — ele rejeita o
        // próprio pedido de token com 400 (invalid_scope). Isso é uma
        // defesa adicional, mais forte do que a policy de scope da API:
        // um scope verdadeiramente desconhecido nunca chega a virar um
        // token, então nunca chega a ser testado pela API.
        var statusCode = await fixture.RequestTokenStatusCodeAsync(
            KeycloakTestRealm.MerchantAAudienceOnlyClientId,
            "totally-unknown-scope-xyz");

        Assert.Equal(HttpStatusCode.BadRequest, statusCode);
    }

    [Fact]
    public async Task Caso6_audience_correta_com_scope_incorreto_resulta_em_403()
    {
        // merchant-a-audience-only-test-client: audience ledger-api sempre
        // presente (mapper no nível do client), consolidation.read
        // disponível como optional scope — permite um token com audience
        // correta para o Ledger e scope correto só para o Consolidation.
        var token = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantAAudienceOnlyClientId, scope: KeycloakTestRealm.ConsolidationReadScope);

        var response = await LedgerEntryRequests.PostEntryAsync(ledgerClient, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Caso7_scope_correto_com_audience_incorreta_resulta_em_401()
    {
        var token = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantBClientId, scope: KeycloakTestRealm.ConsolidationReadScope);

        var response = await LedgerEntryRequests.PostEntryAsync(ledgerClient, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public void Dispose()
    {
        ledgerClient.Dispose();
        consolidationClient.Dispose();
        ledgerFactory.Dispose();
        consolidationFactory.Dispose();
    }
}

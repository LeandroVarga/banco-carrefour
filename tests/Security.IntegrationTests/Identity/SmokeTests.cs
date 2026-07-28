using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.Identity;

[Collection(IdentitySecurityCollection.Name)]
public sealed class SmokeTests(IdentityFixture fixture)
{
    [Fact]
    public async Task Discovery_document_deve_ser_alcancavel_via_HTTPS_com_a_CA_efemera()
    {
        using var client = new HttpClient(fixture.CreateTrustedHandler());

        var response = await client.GetAsync($"{fixture.Authority}/.well-known/openid-configuration");

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(fixture.Authority, document.GetProperty("issuer").GetString());
        Assert.Contains("jwks_uri", document.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Token_real_do_merchant_a_deve_conter_RS256_iss_aud_merchant_id_e_scope()
    {
        var token = await fixture.IssueTokenAsync(Shared.KeycloakTestRealm.MerchantAClientId, scope: "ledger.write");

        var handler = new JwtSecurityTokenHandlerAdapter();
        var (issuer, audiences, merchantId, scope, alg) = handler.Decode(token);

        Assert.Equal("RS256", alg);
        Assert.Equal(fixture.Authority, issuer);
        Assert.Contains("ledger-api", audiences);
        Assert.Equal("merchant-a", merchantId);
        Assert.Contains("ledger.write", scope!.Split(' '));
    }
}

file sealed class JwtSecurityTokenHandlerAdapter
{
    public (string Issuer, IReadOnlyCollection<string> Audiences, string? MerchantId, string? Scope, string Alg) Decode(string token)
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        return (
            jwt.Issuer,
            jwt.Audiences.ToArray(),
            jwt.Claims.FirstOrDefault(c => c.Type == "merchant_id")?.Value,
            jwt.Claims.FirstOrDefault(c => c.Type == "scope")?.Value,
            jwt.Header.Alg);
    }
}

using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace BancoCarrefour.Ledger.IntegrationTests;

/// <summary>
/// Gera tokens RS256 assinados pela chave de teste de <see cref="LedgerApiFactory"/>.
/// Uso exclusivo de testes que não avaliam segurança (o handler de autenticação da
/// própria fábrica substitui a resolução via Authority/JWKS por essa chave estática,
/// só no host de teste — o runtime produtivo nunca usa esta chave nem este caminho).
/// </summary>
internal static class TestJwtTokens
{
    public static string CreateToken(
        string? merchantId,
        DateTime? expires = null,
        string? issuer = null,
        string? audience = null,
        string? scope = "ledger.write")
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "test-user")
        };

        if (merchantId is not null)
        {
            claims.Add(new Claim("merchant_id", merchantId));
        }

        if (!string.IsNullOrEmpty(scope))
        {
            claims.Add(new Claim("scope", scope));
        }

        var credentials = new SigningCredentials(LedgerApiFactory.TestSigningKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: issuer ?? LedgerApiFactory.Issuer,
            audience: audience ?? LedgerApiFactory.Audience,
            claims: claims,
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

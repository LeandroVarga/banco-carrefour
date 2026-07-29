using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace BancoCarrefour.Consolidation.IntegrationTests;

/// <summary>
/// Gera tokens RS256 assinados pela chave de teste de <see cref="ConsolidationApiFactory"/>.
/// Uso exclusivo de testes que não avaliam segurança (o runtime produtivo nunca usa
/// esta chave nem este caminho).
/// </summary>
internal static class ConsolidationTestJwtTokens
{
    public static string CreateToken(
        string? merchantId,
        DateTime? expires = null,
        string? issuer = null,
        string? audience = null,
        string? scope = "consolidation.read")
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

        var credentials = new SigningCredentials(ConsolidationApiFactory.TestSigningKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: issuer ?? ConsolidationApiFactory.Issuer,
            audience: audience ?? ConsolidationApiFactory.Audience,
            claims: claims,
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

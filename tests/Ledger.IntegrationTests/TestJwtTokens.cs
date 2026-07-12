using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BancoCarrefour.Ledger.IntegrationTests;

internal static class TestJwtTokens
{
    public static string CreateToken(string? merchantId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "test-user"),
            new(ClaimTypes.Role, "merchant")
        };

        if (merchantId is not null)
        {
            claims.Add(new Claim("merchant_id", merchantId));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(LedgerApiFactory.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace BancoCarrefour.Security.IntegrationTests.Shared;

/// <summary>
/// Emissor de tokens "de ataque", exclusivo deste projeto de testes, usado
/// somente para os poucos casos que o Keycloak real não pode emitir
/// naturalmente (assinatura inválida, <c>kid</c> desconhecido, token
/// malformado). Usa material RSA efêmero, gerado em memória, nunca
/// persistido em arquivo. Não é referenciado por nenhum código de produção
/// e não substitui o middleware JwtBearer real — apenas fabrica entradas
/// adversariais para provar que o pipeline de validação as rejeita.
/// </summary>
internal sealed class AdversarialJwtIssuer : IDisposable
{
    private readonly RSA foreignKey = RSA.Create(2048);

    /// <summary>
    /// Token assinado por uma chave RSA que não corresponde a nenhuma
    /// entrada do JWKS do Keycloak real — não pode ser produzido pelo
    /// Keycloak (que sempre assina com sua própria chave). Usa um
    /// <c>kid</c> arbitrário, também inexistente no JWKS real.
    /// </summary>
    public string IssueWithForeignKey(
        string issuer,
        string audience,
        string merchantId,
        string scope,
        string kid = "adversarial-unknown-kid",
        DateTime? expires = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "adversarial-test-subject"),
            new("merchant_id", merchantId),
            new("scope", scope)
        };

        var credentials = new SigningCredentials(new RsaSecurityKey(foreignKey) { KeyId = kid }, SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Corrompe apenas o terceiro segmento (assinatura) de um token real,
    /// genuinamente emitido pelo Keycloak — preserva header/payload
    /// (inclusive o <c>kid</c> real), garantindo que a rejeição observada
    /// seja especificamente por falha de assinatura, não por qualquer outra
    /// causa (issuer, audience, kid desconhecido).
    /// </summary>
    public static string CorruptSignature(string genuineToken)
    {
        var segments = genuineToken.Split('.');
        if (segments.Length != 3)
        {
            throw new ArgumentException("Token não tem o formato JWT esperado (header.payload.signature).", nameof(genuineToken));
        }

        var signatureChars = segments[2].ToCharArray();
        for (var i = 0; i < signatureChars.Length; i++)
        {
            signatureChars[i] = signatureChars[i] == 'A' ? 'B' : 'A';
        }

        segments[2] = new string(signatureChars);
        return string.Join('.', segments);
    }

    public void Dispose()
    {
        foreignKey.Dispose();
    }
}

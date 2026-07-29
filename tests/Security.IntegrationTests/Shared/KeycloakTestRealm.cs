namespace BancoCarrefour.Security.IntegrationTests.Shared;

/// <summary>
/// Espelha os nomes reais de realm/clients/scopes definidos em
/// <c>infra/keycloak/realm/banco-carrefour-realm.json</c> — nenhum valor
/// aqui é inventado pelo projeto de testes, todos correspondem a clients
/// efetivamente importados no realm real usado também pelo Docker Compose.
/// </summary>
internal static class KeycloakTestRealm
{
    public const string RealmName = "banco-carrefour";

    public const string LedgerAudience = "ledger-api";
    public const string ConsolidationAudience = "consolidation-api";

    public const string LedgerWriteScope = "ledger.write";
    public const string ConsolidationReadScope = "consolidation.read";

    public const string MerchantAClientId = "merchant-a-test-client";
    public const string MerchantBClientId = "merchant-b-test-client";

    /// <summary>merchant_id=merchant-a, token de vida curta (2s) — usado só para o caso "token expirado".</summary>
    public const string MerchantAShortLivedClientId = "merchant-a-shortlived-test-client";

    /// <summary>Sem mapper de merchant_id — token nunca contém essa claim.</summary>
    public const string MerchantMissingClientId = "merchant-missing-test-client";

    /// <summary>merchant_id hardcoded como string vazia.</summary>
    public const string MerchantBlankClientId = "merchant-blank-test-client";

    /// <summary>merchant_id hardcoded acima do limite de 64 caracteres.</summary>
    public const string MerchantOverflowClientId = "merchant-overflow-test-client";

    /// <summary>
    /// audience ledger-api atribuída no nível do client (não via client
    /// scope) e sem nenhum optional client scope configurado — permite
    /// obter um token com audience correta e SEM nenhum scope operacional,
    /// algo impossível com merchant-a-test-client/merchant-b-test-client
    /// (cujo audience mapper vive dentro do próprio client scope
    /// ledger.write/consolidation.read). Usado só para provar a defesa em
    /// profundidade da policy de scope, independente da validação de
    /// audience do JwtBearer.
    /// </summary>
    public const string MerchantAAudienceOnlyClientId = "merchant-a-audience-only-test-client";
}

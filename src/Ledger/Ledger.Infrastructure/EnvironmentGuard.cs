namespace BancoCarrefour.Ledger.Infrastructure;

/// <summary>
/// Impede que um bypass explícito (<c>SecretsManager:Enabled=false</c>,
/// <c>SystemsManager:Enabled=false</c>) funcione fora do ambiente
/// "Testing" (ADR-0009/ADR-0009): Development, Staging e Production
/// precisam sempre resolver a configuração real via Secrets Manager/SSM —
/// nunca a partir de <c>Enabled=false</c> combinado com valores locais.
/// </summary>
internal static class EnvironmentGuard
{
    public const string TestingEnvironmentName = "Testing";

    public static void EnsureBypassAllowed(string environmentName, string settingName)
    {
        if (!string.Equals(environmentName, TestingEnvironmentName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{settingName}=false só é permitido no ambiente '{TestingEnvironmentName}' " +
                $"— ambiente atual: '{environmentName}'. Development, Staging e Production " +
                "sempre precisam resolver a configuração real (ver ADR-0009/ADR-0009).");
        }
    }
}

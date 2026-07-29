using Microsoft.Extensions.Configuration;

namespace BancoCarrefour.Ledger.Infrastructure.Secrets;

/// <summary>
/// Compõe a connection string do Ledger a partir de <c>ConnectionStrings:Ledger</c>
/// (host/porta/database — não sensível) + credenciais obtidas do Secrets
/// Manager. O bypass (<see cref="SecretsManagerOptions.Enabled"/> = false) é
/// explícito e restrito ao ambiente "Testing" (<see cref="EnvironmentGuard"/>,
/// ADR-0009/ADR-0009): quando desligado, <c>ConnectionStrings:Ledger</c>
/// precisa já estar completa (com credenciais), como os fixtures de teste já
/// fazem. Fora de Testing, Enabled=false falha no startup.
/// </summary>
public static class LedgerConnectionStringResolver
{
    public static string Resolve(IConfiguration configuration, string environmentName)
    {
        var baseConnectionString = configuration.GetConnectionString("Ledger")
            ?? throw new InvalidOperationException("ConnectionStrings:Ledger não configurado.");

        var options = ReadOptions(configuration);

        if (!options.Enabled)
        {
            EnvironmentGuard.EnsureBypassAllowed(environmentName, "SecretsManager:Enabled");
            return baseConnectionString;
        }

        var credentials = DatabaseCredentialsResolver.Resolve(options);
        return $"{baseConnectionString};Username={credentials.Username};Password={credentials.Password}";
    }

    private static SecretsManagerOptions ReadOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(SecretsManagerOptions.SectionName);
        var defaults = new SecretsManagerOptions();

        return new SecretsManagerOptions
        {
            Enabled = section[nameof(SecretsManagerOptions.Enabled)] is { } enabledRaw
                ? bool.Parse(enabledRaw)
                : defaults.Enabled,
            ServiceUrl = section[nameof(SecretsManagerOptions.ServiceUrl)] ?? defaults.ServiceUrl,
            Region = section[nameof(SecretsManagerOptions.Region)] ?? defaults.Region,
            AccessKey = section[nameof(SecretsManagerOptions.AccessKey)] ?? defaults.AccessKey,
            SecretKey = section[nameof(SecretsManagerOptions.SecretKey)] ?? defaults.SecretKey,
            SecretName = section[nameof(SecretsManagerOptions.SecretName)] ?? defaults.SecretName,
            TimeoutSeconds = section[nameof(SecretsManagerOptions.TimeoutSeconds)] is { } timeoutRaw
                ? int.Parse(timeoutRaw)
                : defaults.TimeoutSeconds
        };
    }
}

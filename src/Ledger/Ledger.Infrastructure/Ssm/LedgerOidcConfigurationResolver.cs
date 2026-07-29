using Amazon.Runtime;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Microsoft.Extensions.Configuration;

namespace BancoCarrefour.Ledger.Infrastructure.Ssm;

public sealed record OidcConfiguration(string Authority, string Audience);

/// <summary>
/// Resolve issuer/audience OIDC do Ledger.Api a partir do SSM Parameter
/// Store — fonte autoritativa em runtime oficial (ADR-0009). Lido uma única
/// vez no startup (nunca por request, nunca por polling): depois de
/// resolvido, o valor permanece em memória durante toda a vida da
/// instância — alterar o parâmetro no SSM não afeta um processo já
/// iniciado (requer novo restart/deployment). Falha rápido (exceção,
/// timeout finito, sem retry infinito) se o parâmetro não existir, o SSM
/// estiver indisponível ou o valor resolvido for inválido.
/// </summary>
public static class LedgerOidcConfigurationResolver
{
    public static async Task<OidcConfiguration> ResolveAsync(
        IConfiguration configuration,
        string environmentName,
        CancellationToken cancellationToken = default)
    {
        var options = ReadOptions(configuration);

        if (!options.Enabled)
        {
            EnvironmentGuard.EnsureBypassAllowed(environmentName, "SystemsManager:Enabled");

            var bypassAuthority = configuration["Authentication:Authority"]
                ?? throw new InvalidOperationException(
                    "Authentication:Authority não configurado — necessário quando SystemsManager:Enabled=false (ambiente Testing).");
            var bypassAudience = configuration["Authentication:Audience"]
                ?? throw new InvalidOperationException(
                    "Authentication:Audience não configurado — necessário quando SystemsManager:Enabled=false (ambiente Testing).");

            return Validate(new OidcConfiguration(bypassAuthority, bypassAudience));
        }

        if (string.IsNullOrWhiteSpace(options.IssuerParameterName))
        {
            throw new InvalidOperationException(
                "SystemsManager:IssuerParameterName não configurado — o componente não pode iniciar sem saber qual parâmetro solicitar.");
        }

        if (string.IsNullOrWhiteSpace(options.AudienceParameterName))
        {
            throw new InvalidOperationException(
                "SystemsManager:AudienceParameterName não configurado — o componente não pode iniciar sem saber qual parâmetro solicitar.");
        }

        using var client = new AmazonSimpleSystemsManagementClient(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonSimpleSystemsManagementConfig
            {
                ServiceURL = options.ServiceUrl,
                AuthenticationRegion = options.Region,
                Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
            });

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        string issuer;
        string audience;
        try
        {
            var issuerResponse = await client.GetParameterAsync(
                new GetParameterRequest { Name = options.IssuerParameterName },
                linkedCts.Token);
            issuer = issuerResponse.Parameter.Value;

            var audienceResponse = await client.GetParameterAsync(
                new GetParameterRequest { Name = options.AudienceParameterName },
                linkedCts.Token);
            audience = audienceResponse.Parameter.Value;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Falha ao obter configuração OIDC do SSM (parâmetros '{options.IssuerParameterName}'/'{options.AudienceParameterName}') " +
                "— o componente não pode iniciar sem essa configuração.", ex);
        }

        return Validate(new OidcConfiguration(issuer, audience));
    }

    private static OidcConfiguration Validate(OidcConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.Authority)
            || !Uri.TryCreate(configuration.Authority, UriKind.Absolute, out var authorityUri)
            || authorityUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Issuer OIDC resolvido é inválido — precisa ser uma URI absoluta HTTPS não vazia.");
        }

        if (string.IsNullOrWhiteSpace(configuration.Audience)
            || configuration.Audience != configuration.Audience.Trim())
        {
            throw new InvalidOperationException(
                "Audience OIDC resolvida é inválida — precisa ser não vazia e sem espaços nas extremidades.");
        }

        return configuration;
    }

    private static SystemsManagerOptions ReadOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(SystemsManagerOptions.SectionName);
        var defaults = new SystemsManagerOptions();

        return new SystemsManagerOptions
        {
            Enabled = section[nameof(SystemsManagerOptions.Enabled)] is { } enabledRaw
                ? bool.Parse(enabledRaw)
                : defaults.Enabled,
            ServiceUrl = section[nameof(SystemsManagerOptions.ServiceUrl)] ?? defaults.ServiceUrl,
            Region = section[nameof(SystemsManagerOptions.Region)] ?? defaults.Region,
            AccessKey = section[nameof(SystemsManagerOptions.AccessKey)] ?? defaults.AccessKey,
            SecretKey = section[nameof(SystemsManagerOptions.SecretKey)] ?? defaults.SecretKey,
            IssuerParameterName = section[nameof(SystemsManagerOptions.IssuerParameterName)] ?? defaults.IssuerParameterName,
            AudienceParameterName = section[nameof(SystemsManagerOptions.AudienceParameterName)] ?? defaults.AudienceParameterName,
            TimeoutSeconds = section[nameof(SystemsManagerOptions.TimeoutSeconds)] is { } timeoutRaw
                ? int.Parse(timeoutRaw)
                : defaults.TimeoutSeconds
        };
    }
}

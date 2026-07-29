namespace BancoCarrefour.Ledger.Infrastructure.Ssm;

/// <summary>
/// Configuração de acesso ao SSM Parameter Store (LocalStack em ambiente
/// local) usada para resolver issuer/audience OIDC no startup — fonte
/// autoritativa quando <see cref="Enabled"/> = true (ADR-0009):
/// <c>Authentication:Authority</c>/<c>Authentication:Audience</c> em
/// appsettings, Compose ou variáveis concorrentes não funcionam como
/// fallback silencioso. <see cref="Enabled"/> = false é um bypass explícito,
/// restrito ao ambiente "Testing" (ver <see cref="EnvironmentGuard"/>).
/// </summary>
public sealed class SystemsManagerOptions
{
    public const string SectionName = "SystemsManager";

    public bool Enabled { get; set; } = true;

    public string ServiceUrl { get; set; } = "http://localstack:4566";

    public string Region { get; set; } = "us-east-1";

    public string AccessKey { get; set; } = "test";

    public string SecretKey { get; set; } = "test";

    public string IssuerParameterName { get; set; } = string.Empty;

    public string AudienceParameterName { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 5;
}

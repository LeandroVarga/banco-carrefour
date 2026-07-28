namespace BancoCarrefour.Consolidation.Infrastructure.Secrets;

/// <summary>
/// Configuração de acesso ao AWS Secrets Manager (LocalStack em ambiente
/// local). <see cref="Enabled"/> = false é um bypass explícito, restrito a
/// ambiente local/testes (ver ADR-0009): quando desligado,
/// <c>ConnectionStrings:Consolidation</c> é usada tal como está, já com
/// credenciais completas (nunca usado em Production).
/// </summary>
public sealed class SecretsManagerOptions
{
    public const string SectionName = "SecretsManager";

    public bool Enabled { get; set; } = true;

    public string ServiceUrl { get; set; } = "http://localstack:4566";

    public string Region { get; set; } = "us-east-1";

    public string AccessKey { get; set; } = "test";

    public string SecretKey { get; set; } = "test";

    public string SecretName { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 5;
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace BancoCarrefour.Ledger.Infrastructure.Secrets;

public sealed record DatabaseCredentials(string Username, string Password);

/// <summary>
/// Resolve credenciais de banco a partir do AWS Secrets Manager. Falha
/// rápido (exceção) no startup se o secret obrigatório não existir ou não
/// tiver o schema esperado — nunca loga o valor obtido (ADR-0009).
/// </summary>
public static class DatabaseCredentialsResolver
{
    public static DatabaseCredentials Resolve(SecretsManagerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SecretName))
        {
            throw new InvalidOperationException(
                "SecretsManager:SecretName não configurado — o componente não pode iniciar sem saber qual secret solicitar.");
        }

        using var client = new AmazonSecretsManagerClient(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonSecretsManagerConfig
            {
                ServiceURL = options.ServiceUrl,
                AuthenticationRegion = options.Region,
                Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
            });

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));

        GetSecretValueResponse response;
        try
        {
            response = client.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = options.SecretName
            }, timeoutCts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Falha ao obter o secret '{options.SecretName}' do Secrets Manager — o componente não pode iniciar sem essa credencial.", ex);
        }

        if (response.SecretString is null)
        {
            throw new InvalidOperationException(
                $"O secret '{options.SecretName}' não contém SecretString (valor ausente) — o componente não pode iniciar sem essa credencial.");
        }

        DatabaseCredentialsPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<DatabaseCredentialsPayload>(response.SecretString);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"O secret '{options.SecretName}' não contém um payload JSON válido no schema esperado.", ex);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Username) || string.IsNullOrWhiteSpace(payload.Password))
        {
            throw new InvalidOperationException(
                $"O secret '{options.SecretName}' não contém os campos obrigatórios 'username'/'password'.");
        }

        return new DatabaseCredentials(payload.Username, payload.Password);
    }

    private sealed record DatabaseCredentialsPayload(
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("password")] string? Password);
}

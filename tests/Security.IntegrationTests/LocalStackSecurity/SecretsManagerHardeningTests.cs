using System.Diagnostics;
using System.Text;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using BancoCarrefour.Ledger.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.LocalStackSecurity;

/// <summary>
/// Endurecimento do Secrets Manager (ADR-0009): o bypass explícito
/// (<c>SecretsManager:Enabled=false</c>) só é permitido no ambiente
/// "Testing" (<see cref="EnvironmentGuard"/>, verificado indiretamente via
/// <see cref="LedgerConnectionStringResolver"/>) - qualquer outro ambiente
/// com Enabled=false falha no startup. Cobre também as falhas de
/// <see cref="DatabaseCredentialsResolver"/> exigidas pela auditoria:
/// serviço indisponível, secret inexistente, SecretString ausente, JSON
/// inválido, username/password ausentes ou vazios, timeout finito (sem
/// retry infinito). Nenhuma mensagem de erro deve conter senha, payload ou
/// connection string - verificado explicitamente onde aplicável.
/// </summary>
[Collection(LocalStackSecurityCollection.Name)]
public sealed class SecretsManagerHardeningTests(LocalStackSecurityFixture fixture)
{
    [Fact]
    public void Testing_com_Enabled_false_e_connection_string_completa_e_permitido()
    {
        const string fullConnectionString = "Host=ledger-postgres;Port=5432;Database=ledger;Username=ledger_full;Password=already-set";
        var configuration = BuildConfiguration(
            connectionString: fullConnectionString,
            enabled: false,
            secretName: string.Empty);

        var result = LedgerConnectionStringResolver.Resolve(configuration, "Testing");

        Assert.Equal(fullConnectionString, result);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Ambiente_diferente_de_Testing_com_Enabled_false_falha_no_startup(string environmentName)
    {
        var configuration = BuildConfiguration(
            connectionString: "Host=ledger-postgres;Port=5432;Database=ledger;Username=ledger_full;Password=x",
            enabled: false,
            secretName: string.Empty);

        var ex = Assert.Throws<InvalidOperationException>(
            () => LedgerConnectionStringResolver.Resolve(configuration, environmentName));

        Assert.Contains("Testing", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_com_Enabled_true_e_secret_inexistente_falha()
    {
        var configuration = BuildConfiguration(
            connectionString: "Host=ledger-postgres;Port=5432;Database=ledger",
            enabled: true,
            secretName: $"banco-carrefour/secret-que-nao-existe-{Guid.NewGuid():N}");

        Assert.Throws<InvalidOperationException>(
            () => LedgerConnectionStringResolver.Resolve(configuration, "Production"));
    }

    [Fact]
    public async Task Production_com_Enabled_true_e_secret_valido_e_permitido()
    {
        var repositoryRoot = LocalStackSecurityFixture.LocateRepositoryRoot();
        var password = $"pwd-{Guid.NewGuid():N}";
        var bootstrapResult = await fixture.RunSecretValueBootstrapAsync(
            repositoryRoot, password, "irrelevante-1", "irrelevante-2", "irrelevante-3");
        Assert.True(bootstrapResult.ExitCode == 0, $"secret-value-bootstrap falhou: {bootstrapResult.Stderr}");

        var configuration = BuildConfiguration(
            connectionString: "Host=ledger-postgres;Port=5432;Database=ledger",
            enabled: true,
            secretName: LocalStackSecurityFixture.LedgerApiSecretName);

        var result = LedgerConnectionStringResolver.Resolve(configuration, "Production");

        Assert.Contains("Username=ledger_api", result, StringComparison.Ordinal);
        Assert.Contains($"Password={password}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Servico_indisponivel_falha_rapido_com_timeout_finito_sem_retry_infinito()
    {
        var options = new SecretsManagerOptions
        {
            ServiceUrl = "http://127.0.0.1:1",
            Region = LocalStackSecurityFixture.Region,
            AccessKey = LocalStackSecurityFixture.AccessKey,
            SecretKey = LocalStackSecurityFixture.SecretKey,
            SecretName = LocalStackSecurityFixture.LedgerApiSecretName,
            TimeoutSeconds = 3
        };

        var stopwatch = Stopwatch.StartNew();
        var ex = Assert.Throws<InvalidOperationException>(() => DatabaseCredentialsResolver.Resolve(options));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Esperava falha rápida e finita (<15s), levou {stopwatch.Elapsed}.");
        Assert.DoesNotContain(LocalStackSecurityFixture.SecretKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Secret_inexistente_causa_falha_clara()
    {
        var options = new SecretsManagerOptions
        {
            ServiceUrl = fixture.ServiceUrl,
            Region = LocalStackSecurityFixture.Region,
            AccessKey = LocalStackSecurityFixture.AccessKey,
            SecretKey = LocalStackSecurityFixture.SecretKey,
            SecretName = $"banco-carrefour/nao-existe-{Guid.NewGuid():N}"
        };

        Assert.Throws<InvalidOperationException>(() => DatabaseCredentialsResolver.Resolve(options));
    }

    [Fact]
    public async Task SecretString_ausente_secret_binario_causa_falha_clara()
    {
        using var client = CreateSecretsManagerClient();
        var secretName = await CreateTemporarySecretAsync(client, "SecretString ausente (binário)");

        await client.PutSecretValueAsync(new PutSecretValueRequest
        {
            SecretId = secretName,
            SecretBinary = new MemoryStream(Encoding.UTF8.GetBytes("valor-binario-sem-secretstring"))
        });

        var options = BuildOptionsFor(secretName);
        var ex = Assert.Throws<InvalidOperationException>(() => DatabaseCredentialsResolver.Resolve(options));
        Assert.Contains("SecretString", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_invalido_no_secret_causa_falha_clara()
    {
        using var client = CreateSecretsManagerClient();
        var secretName = await CreateTemporarySecretAsync(client, "JSON inválido");
        await client.PutSecretValueAsync(new PutSecretValueRequest { SecretId = secretName, SecretString = "isto-nao-e-json" });

        var options = BuildOptionsFor(secretName);
        var ex = Assert.Throws<InvalidOperationException>(() => DatabaseCredentialsResolver.Resolve(options));
        Assert.Contains("JSON", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Username_ausente_no_secret_causa_falha_clara()
    {
        using var client = CreateSecretsManagerClient();
        var secretName = await CreateTemporarySecretAsync(client, "username ausente");
        await client.PutSecretValueAsync(new PutSecretValueRequest { SecretId = secretName, SecretString = """{"password":"x"}""" });

        var options = BuildOptionsFor(secretName);
        Assert.Throws<InvalidOperationException>(() => DatabaseCredentialsResolver.Resolve(options));
    }

    [Fact]
    public async Task Username_vazio_no_secret_causa_falha_clara()
    {
        using var client = CreateSecretsManagerClient();
        var secretName = await CreateTemporarySecretAsync(client, "username vazio");
        await client.PutSecretValueAsync(new PutSecretValueRequest { SecretId = secretName, SecretString = """{"username":"","password":"x"}""" });

        var options = BuildOptionsFor(secretName);
        Assert.Throws<InvalidOperationException>(() => DatabaseCredentialsResolver.Resolve(options));
    }

    [Fact]
    public async Task Password_ausente_no_secret_causa_falha_clara()
    {
        using var client = CreateSecretsManagerClient();
        var secretName = await CreateTemporarySecretAsync(client, "password ausente");
        await client.PutSecretValueAsync(new PutSecretValueRequest { SecretId = secretName, SecretString = """{"username":"x"}""" });

        var options = BuildOptionsFor(secretName);
        Assert.Throws<InvalidOperationException>(() => DatabaseCredentialsResolver.Resolve(options));
    }

    [Fact]
    public async Task Password_vazia_no_secret_causa_falha_clara()
    {
        using var client = CreateSecretsManagerClient();
        var secretName = await CreateTemporarySecretAsync(client, "password vazia");
        await client.PutSecretValueAsync(new PutSecretValueRequest { SecretId = secretName, SecretString = """{"username":"x","password":""}""" });

        var options = BuildOptionsFor(secretName);
        Assert.Throws<InvalidOperationException>(() => DatabaseCredentialsResolver.Resolve(options));
    }

    private async Task<string> CreateTemporarySecretAsync(AmazonSecretsManagerClient client, string description)
    {
        var secretName = $"banco-carrefour/teste-hardening/{Guid.NewGuid():N}";
        await client.CreateSecretAsync(new CreateSecretRequest
        {
            Name = secretName,
            Description = $"Secret temporário de teste ({description}) - LocalStackSecurityTests/SecretsManagerHardeningTests."
        });

        return secretName;
    }

    private SecretsManagerOptions BuildOptionsFor(string secretName) => new()
    {
        ServiceUrl = fixture.ServiceUrl,
        Region = LocalStackSecurityFixture.Region,
        AccessKey = LocalStackSecurityFixture.AccessKey,
        SecretKey = LocalStackSecurityFixture.SecretKey,
        SecretName = secretName
    };

    private IConfiguration BuildConfiguration(string connectionString, bool enabled, string secretName)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Ledger"] = connectionString,
            ["SecretsManager:Enabled"] = enabled ? "true" : "false",
            ["SecretsManager:ServiceUrl"] = fixture.ServiceUrl,
            ["SecretsManager:Region"] = LocalStackSecurityFixture.Region,
            ["SecretsManager:AccessKey"] = LocalStackSecurityFixture.AccessKey,
            ["SecretsManager:SecretKey"] = LocalStackSecurityFixture.SecretKey,
            ["SecretsManager:SecretName"] = secretName
        };

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private AmazonSecretsManagerClient CreateSecretsManagerClient() =>
        new(new BasicAWSCredentials(LocalStackSecurityFixture.AccessKey, LocalStackSecurityFixture.SecretKey),
            new AmazonSecretsManagerConfig { ServiceURL = fixture.ServiceUrl, AuthenticationRegion = LocalStackSecurityFixture.Region });
}

using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using BancoCarrefour.Ledger.Infrastructure.Secrets;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.LocalStackSecurity;

/// <summary>
/// Prova, contra um LocalStack real e os módulos Terraform reais (ver
/// <see cref="LocalStackSecurityFixture"/>), o comportamento de ponta a
/// ponta do bloco Secrets Manager/SSM/KMS/IAM (ADR-0009).
/// Nenhum teste afirma isolamento negativo por IAM (confirmado por
/// capability spike empírico) — o LocalStack Hobby usado neste projeto
/// não comprova enforcement de policy.
/// </summary>
[Collection(LocalStackSecurityCollection.Name)]
public sealed class LocalStackSecurityTests
{
    private readonly LocalStackSecurityFixture fixture;

    public LocalStackSecurityTests(LocalStackSecurityFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void Caso_1_Secrets_existem_apos_terraform_apply()
    {
        Assert.Equal(4, fixture.SecretArns.Count);
        Assert.Contains(LocalStackSecurityFixture.LedgerApiSecretName, fixture.SecretArns.Keys);
        Assert.Contains(LocalStackSecurityFixture.LedgerOutboxPublisherSecretName, fixture.SecretArns.Keys);
        Assert.Contains(LocalStackSecurityFixture.ConsolidationApiSecretName, fixture.SecretArns.Keys);
        Assert.Contains(LocalStackSecurityFixture.ConsolidationWorkerSecretName, fixture.SecretArns.Keys);

        foreach (var arn in fixture.SecretArns.Values)
        {
            Assert.StartsWith("arn:aws:secretsmanager:", arn);
        }
    }

    [Fact]
    public async Task Caso_2_Secret_nao_possui_valor_antes_do_bootstrap()
    {
        // Este teste roda ANTES de qualquer chamada a RunSecretValueBootstrapAsync
        // nesta classe: aws_secretsmanager_secret (sem _version) não tem nenhuma
        // versão corrente - GetSecretValue deve falhar.
        using var client = CreateSecretsManagerClient();

        var ex = await Assert.ThrowsAsync<Amazon.SecretsManager.Model.ResourceNotFoundException>(() => client.GetSecretValueAsync(new GetSecretValueRequest
        {
            SecretId = LocalStackSecurityFixture.LedgerApiSecretName
        }));

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task Caso_3_a_9_Bootstrap_grava_recupera_e_e_idempotente_sem_logar_valor()
    {
        var repositoryRoot = LocalStackSecurityFixture.LocateRepositoryRoot();
        var ledgerApiPassword = $"lap-{Guid.NewGuid():N}";
        var ledgerOutboxPassword = $"lop-{Guid.NewGuid():N}";
        var consolidationApiPassword = $"cap-{Guid.NewGuid():N}";
        var consolidationWorkerPassword = $"cwp-{Guid.NewGuid():N}";

        // Caso 3: secret-value-bootstrap grava valores (script real, não reimplementado).
        var firstRun = await fixture.RunSecretValueBootstrapAsync(
            repositoryRoot, ledgerApiPassword, ledgerOutboxPassword, consolidationApiPassword, consolidationWorkerPassword);
        Assert.True(firstRun.ExitCode == 0, $"secret-value-bootstrap (1a execução) falhou: {firstRun.Stderr}");

        // Caso 8: logs (stdout+stderr do próprio bootstrap) não exibem o valor da senha.
        Assert.DoesNotContain(ledgerApiPassword, firstRun.Stdout);
        Assert.DoesNotContain(ledgerApiPassword, firstRun.Stderr);
        Assert.DoesNotContain(consolidationWorkerPassword, firstRun.Stdout);
        Assert.DoesNotContain(consolidationWorkerPassword, firstRun.Stderr);

        // Caso 4/9: valores podem ser recuperados pela aplicação, usando o
        // próprio DatabaseCredentialsResolver de produção (não reimplementado)
        // e SOMENTE o secret configurado para aquele componente.
        var ledgerApiCredentials = DatabaseCredentialsResolver.Resolve(new SecretsManagerOptions
        {
            ServiceUrl = fixture.ServiceUrl,
            Region = LocalStackSecurityFixture.Region,
            AccessKey = LocalStackSecurityFixture.AccessKey,
            SecretKey = LocalStackSecurityFixture.SecretKey,
            SecretName = LocalStackSecurityFixture.LedgerApiSecretName
        });
        Assert.Equal("ledger_api", ledgerApiCredentials.Username);
        Assert.Equal(ledgerApiPassword, ledgerApiCredentials.Password);

        var consolidationWorkerCredentials = DatabaseCredentialsResolver.Resolve(new SecretsManagerOptions
        {
            ServiceUrl = fixture.ServiceUrl,
            Region = LocalStackSecurityFixture.Region,
            AccessKey = LocalStackSecurityFixture.AccessKey,
            SecretKey = LocalStackSecurityFixture.SecretKey,
            SecretName = LocalStackSecurityFixture.ConsolidationWorkerSecretName
        });
        Assert.Equal("consolidation_worker", consolidationWorkerCredentials.Username);
        Assert.Equal(consolidationWorkerPassword, consolidationWorkerCredentials.Password);

        // Caso 5: bootstrap é idempotente - reexecutar com os MESMOS valores não falha.
        var secondRun = await fixture.RunSecretValueBootstrapAsync(
            repositoryRoot, ledgerApiPassword, ledgerOutboxPassword, consolidationApiPassword, consolidationWorkerPassword);
        Assert.True(secondRun.ExitCode == 0, $"secret-value-bootstrap (2a execução, idempotente) falhou: {secondRun.Stderr}");

        // Caso 6/7: rotação altera o valor - o valor antigo deixa de ser o corrente.
        var rotatedLedgerApiPassword = $"lap-rotated-{Guid.NewGuid():N}";
        var rotationRun = await fixture.RunSecretValueBootstrapAsync(
            repositoryRoot, rotatedLedgerApiPassword, ledgerOutboxPassword, consolidationApiPassword, consolidationWorkerPassword);
        Assert.True(rotationRun.ExitCode == 0, $"secret-value-bootstrap (rotação) falhou: {rotationRun.Stderr}");

        var afterRotation = DatabaseCredentialsResolver.Resolve(new SecretsManagerOptions
        {
            ServiceUrl = fixture.ServiceUrl,
            Region = LocalStackSecurityFixture.Region,
            AccessKey = LocalStackSecurityFixture.AccessKey,
            SecretKey = LocalStackSecurityFixture.SecretKey,
            SecretName = LocalStackSecurityFixture.LedgerApiSecretName
        });
        Assert.Equal(rotatedLedgerApiPassword, afterRotation.Password);
        Assert.NotEqual(ledgerApiPassword, afterRotation.Password);
    }

    [Fact]
    public async Task Caso_secret_inexistente_causa_falha_clara()
    {
        using var client = CreateSecretsManagerClient();

        var ex = await Assert.ThrowsAsync<Amazon.SecretsManager.Model.ResourceNotFoundException>(() => client.PutSecretValueAsync(new PutSecretValueRequest
        {
            SecretId = "banco-carrefour/secret-que-nao-existe",
            SecretString = "{\"username\":\"x\",\"password\":\"y\"}"
        }));

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task Caso_10_SSM_retorna_configuracao_nao_sensivel()
    {
        using var client = CreateSsmClient();

        var issuer = await client.GetParameterAsync(new GetParameterRequest { Name = "/banco-carrefour/oidc/issuer" });
        var ledgerAudience = await client.GetParameterAsync(new GetParameterRequest { Name = "/banco-carrefour/oidc/ledger-audience" });
        var consolidationAudience = await client.GetParameterAsync(new GetParameterRequest { Name = "/banco-carrefour/oidc/consolidation-audience" });

        Assert.Equal("https://keycloak.localhost:8443/realms/banco-carrefour", issuer.Parameter.Value);
        Assert.Equal("ledger-api", ledgerAudience.Parameter.Value);
        Assert.Equal("consolidation-api", consolidationAudience.Parameter.Value);
        Assert.Equal(ParameterType.String, issuer.Parameter.Type);
    }

    [Fact]
    public async Task Caso_11_KMS_encrypt_decrypt_funciona()
    {
        using var client = CreateKmsClient();
        const string plaintext = "ciclo3-kms-roundtrip-probe";

        var encryptResponse = await client.EncryptAsync(new EncryptRequest
        {
            KeyId = fixture.KmsKeyId,
            Plaintext = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(plaintext))
        });

        Assert.NotEmpty(encryptResponse.CiphertextBlob.ToArray());

        var decryptResponse = await client.DecryptAsync(new DecryptRequest
        {
            CiphertextBlob = encryptResponse.CiphertextBlob
        });

        var recovered = System.Text.Encoding.UTF8.GetString(decryptResponse.Plaintext.ToArray());
        Assert.Equal(plaintext, recovered);
    }

    [Fact]
    public async Task Caso_12_IAM_resources_foram_provisionados()
    {
        using var client = CreateIamClient();

        Assert.Equal(4, fixture.IamRoleArns.Count);

        foreach (var (roleName, arn) in fixture.IamRoleArns)
        {
            var role = await client.GetRoleAsync(new GetRoleRequest { RoleName = roleName });
            Assert.Equal(arn, role.Role.Arn);

            var policies = await client.ListRolePoliciesAsync(new ListRolePoliciesRequest { RoleName = roleName });
            Assert.Contains(policies.PolicyNames, name => name.EndsWith("least-privilege", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Caso_13_Terraform_state_nao_contem_valores_sentinela()
    {
        var repositoryRoot = LocalStackSecurityFixture.LocateRepositoryRoot();
        var sentinel = $"sentinela-nunca-deve-aparecer-no-state-{Guid.NewGuid():N}";

        var bootstrapResult = await fixture.RunSecretValueBootstrapAsync(
            repositoryRoot, sentinel, "irrelevante-1", "irrelevante-2", "irrelevante-3");
        Assert.True(bootstrapResult.ExitCode == 0, $"secret-value-bootstrap falhou: {bootstrapResult.Stderr}");

        var stateRaw = await fixture.ReadTerraformStateRawAsync();

        Assert.DoesNotContain(sentinel, stateRaw);
    }

    private AmazonSecretsManagerClient CreateSecretsManagerClient() =>
        new(new BasicAWSCredentials(LocalStackSecurityFixture.AccessKey, LocalStackSecurityFixture.SecretKey),
            new AmazonSecretsManagerConfig { ServiceURL = fixture.ServiceUrl, AuthenticationRegion = LocalStackSecurityFixture.Region });

    private AmazonSimpleSystemsManagementClient CreateSsmClient() =>
        new(new BasicAWSCredentials(LocalStackSecurityFixture.AccessKey, LocalStackSecurityFixture.SecretKey),
            new AmazonSimpleSystemsManagementConfig { ServiceURL = fixture.ServiceUrl, AuthenticationRegion = LocalStackSecurityFixture.Region });

    private AmazonKeyManagementServiceClient CreateKmsClient() =>
        new(new BasicAWSCredentials(LocalStackSecurityFixture.AccessKey, LocalStackSecurityFixture.SecretKey),
            new AmazonKeyManagementServiceConfig { ServiceURL = fixture.ServiceUrl, AuthenticationRegion = LocalStackSecurityFixture.Region });

    private AmazonIdentityManagementServiceClient CreateIamClient() =>
        new(new BasicAWSCredentials(LocalStackSecurityFixture.AccessKey, LocalStackSecurityFixture.SecretKey),
            new AmazonIdentityManagementServiceConfig { ServiceURL = fixture.ServiceUrl, AuthenticationRegion = LocalStackSecurityFixture.Region });
}

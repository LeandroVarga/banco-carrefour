using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BancoCarrefour.Consolidation.Infrastructure;
using BancoCarrefour.Consolidation.Infrastructure.Entities;
using BancoCarrefour.Ledger.Infrastructure;
using BancoCarrefour.Security.IntegrationTests.Shared;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.Edge;

[CollectionDefinition(Name)]
public sealed class AuthenticatedEdgeFlowCollection : ICollectionFixture<AuthenticatedEdgeFlowFixture>
{
    public const string Name = "AuthenticatedEdgeFlow";
}

/// <summary>
/// Envia todo request para um Host HTTP fixo — usado só para os
/// chamadores administrativos/de token deste teste alcançarem o vhost
/// <c>keycloak.localhost</c> do edge-proxy real, mesmo conectando via
/// <c>127.0.0.1:&lt;porta aleatória do Testcontainers&gt;</c> (a validação
/// TLS/SNI já usa o certificado efêmero via
/// <see cref="EphemeralCertificateAuthority.ValidateServerCertificate"/>;
/// isto só corrige o roteamento por <c>server_name</c> do nginx).
/// </summary>
file sealed class HostRewritingHandler(string host, HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Host = host;
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Replica a topologia real mínima necessária para provar, de ponta a
/// ponta e de forma automatizada, o que já havia sido validado manualmente
/// nesta sessão (ver "Fluxo de autenticação" no documento do ciclo):
/// Keycloak real (HTTPS efêmero) + edge-proxy real (mesma imagem/config,
/// TLS/WAF) + Ledger.Api/Consolidation.Api reais (mesmas imagens já
/// construídas por <c>docker compose build</c>, reaproveitadas, não
/// reconstruídas) + Postgres reais, todos na mesma rede com os aliases que
/// o template nginx real espera (<c>keycloak</c>, <c>ledger-api</c>,
/// <c>consolidation-api</c>, <c>ledger-postgres</c>,
/// <c>consolidation-postgres</c>) e o alias <c>keycloak.localhost</c> no
/// próprio edge-proxy (idêntico ao <c>networks.default.aliases</c> do
/// docker-compose.yml real). O fluxo Outbox→SQS→Worker→Projection fica
/// fora de escopo aqui (fixture sistêmica futura) — por isso a consulta ao
/// Consolidation é feita contra uma projeção inserida diretamente (mesma
/// técnica de <see cref="Identity.MerchantIsolationTests"/>), não através
/// do pipeline assíncrono completo.
///
/// Issuer/audience das duas APIs vêm do SSM real (LocalStack + módulos
/// Terraform reais de <c>infra/terraform/environments/localstack-hobby</c>,
/// aplicados aqui com a mesma técnica de cópia para /tmp usada em
/// <see cref="LocalStackSecurity.LocalStackSecurityFixture"/>) - não do
/// bypass <c>Authentication__Authority/Audience</c>: os dois testes desta
/// fixture (POST 201 do Ledger, GET funcional do Consolidation) passam a
/// ser a prova de ponta a ponta do fluxo SSM autoritativo (ADR-0009). O
/// Secrets Manager permanece em bypass explícito (<c>SecretsManager__Enabled=false</c>) porque a rotação/gestão de credenciais de banco já é
/// coberta separadamente por <see cref="LocalStackSecurity.LocalStackSecurityTests"/>.
/// </summary>
public sealed class AuthenticatedEdgeFlowFixture : IAsyncLifetime
{
    private const string KeycloakImage = "quay.io/keycloak/keycloak:26.7.0";
    private const string EdgeProxyImage = "owasp/modsecurity-crs:nginx";
    private const string LedgerApiImage = "banco-carrefour-ledger-api:local";
    private const string ConsolidationApiImage = "banco-carrefour-consolidation-api:local";
    private const string KeycloakHostHeader = "keycloak.localhost:8443";
    private const string LocalStackRegion = "us-east-1";
    private const string LocalStackAccessKey = "test";
    private const string LocalStackSecretKey = "test";
    private const string TerraformWorkDir = "/tmp/terraform/environments/localstack-hobby";

    private readonly EphemeralCertificateAuthority certificateAuthority = new();
    private readonly string bootstrapAdminClientId = $"bootstrap-{Guid.NewGuid():N}";
    private readonly string bootstrapAdminClientSecret = $"{Guid.NewGuid():N}{Guid.NewGuid():N}";

    private INetwork network = null!;
    private IContainer localStack = null!;
    private IContainer terraformRunner = null!;
    private PostgreSqlContainer ledgerPostgres = null!;
    private PostgreSqlContainer consolidationPostgres = null!;
    private IContainer keycloak = null!;
    private IContainer ledgerApi = null!;
    private IContainer consolidationApi = null!;
    private IContainer edgeProxy = null!;
    private HttpClient edgeClient = null!;
    private HttpClient keycloakViaEdgeClient = null!;
    private HttpClient directKeycloakClient = null!;
    private KeycloakAdminClient adminClient = null!;
    private KeycloakAdminClient directAdminClient = null!;
    private string merchantAClientSecret = null!;

    public string EdgeHttpsBaseUrl => $"https://127.0.0.1:{edgeProxy.GetMappedPublicPort(8443)}";

    /// <summary>
    /// Endpoint do LocalStack alcançável a partir do processo de teste (host)
    /// - usado só pela prova de semântica de restart do SSM
    /// (<see cref="AuthenticatedEdgeFlowTests"/>), que precisa alterar um
    /// parâmetro real do SSM a partir do processo de teste, fora da rede
    /// interna dos containers.
    /// </summary>
    public string LocalStackServiceUrl => $"http://{localStack.Hostname}:{localStack.GetMappedPublicPort(4566)}";

    public HttpClientHandler CreateTrustedHandler()
    {
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = certificateAuthority.ValidateServerCertificate
        };
    }

    /// <summary>
    /// Reinicia SOMENTE o container real do Ledger.Api já provisionado por
    /// esta fixture (nunca recria a topologia) - usado para provar a
    /// semântica real de restart do SSM: um valor alterado no SSM só entra
    /// em vigor depois que o processo é reiniciado (ADR-0009).
    /// </summary>
    public async Task RestartLedgerApiAsync()
    {
        await ledgerApi.StopAsync();
        await ledgerApi.StartAsync();

        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{EdgeHttpsBaseUrl}/ledger/health/ready");
                request.Headers.Host = "localhost:8443";
                using var response = await edgeClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // ainda reiniciando — tenta de novo
            }

            await Task.Delay(2000);
        }

        throw new TimeoutException("ledger-api não ficou saudável a tempo após o restart.");
    }

    public async Task<string> IssueMerchantATokenAsync(string scope)
    {
        return await adminClient.GetTokenAsync(KeycloakTestRealm.MerchantAClientId, merchantAClientSecret, scope);
    }

    /// <summary>
    /// Altera, no Keycloak real (Admin REST API, client scope
    /// <c>ledger.write</c>, mapper <c>ledger-api-audience</c>), a audience
    /// que passa a ser emitida em tokens novos — usado só para provar que,
    /// depois do restart, a nova audience configurada via SSM realmente fica
    /// operacional (não apenas que a antiga deixou de funcionar). Busca um
    /// token de administrador novo a cada chamada, evitando qualquer
    /// suposição sobre o tempo de vida do token obtido em
    /// <see cref="InitializeAsync"/>.
    /// </summary>
    public async Task SetLedgerAudienceMapperAsync(string audienceValue)
    {
        var adminToken = await directAdminClient.GetTokenAsync(bootstrapAdminClientId, bootstrapAdminClientSecret, realmOverride: "master");
        await directAdminClient.SetAudienceMapperValueAsync(adminToken, "ledger.write", "ledger-api-audience", audienceValue);
    }

    public async Task InsertConsolidationDailyBalanceAsync(string merchantId, DateOnly businessDate)
    {
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(consolidationPostgres.GetConnectionString())
            .Options;
        await using var dbContext = new ConsolidationDbContext(options);

        dbContext.DailyBalances.Add(new DailyBalance
        {
            DailyBalanceId = Guid.NewGuid(),
            MerchantId = merchantId,
            BusinessDate = businessDate,
            TotalCredits = 150.7m,
            TotalDebits = 25.1m,
            Balance = 125.6m,
            Currency = "BRL",
            EntryCount = 2,
            LastEventOccurredAt = DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            LastUpdatedAt = DateTimeOffset.Parse("2026-07-11T13:45:05Z")
        });

        await dbContext.SaveChangesAsync();
    }

    public async Task InitializeAsync()
    {
        network = new NetworkBuilder().Build();
        await network.CreateAsync();

        var repositoryRootForTerraform = LocateRepositoryRoot();

        // Mesma lista de SERVICES do docker-compose.yml real (o ambiente
        // localstack-hobby aplica o módulo messaging - SQS - junto com
        // secrets/parameters/kms/iam, ver LocalStackSecurityFixture).
        // Mesma versão exata fixada em docker-compose.yml (digest
        // sha256:3ebc37595918b8accb852f8048fef2aff047d465167edd655528065b07bc364a) -
        // WithImage(string) não aceita "tag@digest" no Testcontainers 3.10.0
        // (ver comentário equivalente em IdentityFixture.cs para o Keycloak).
        localStack = new ContainerBuilder()
            .WithImage("localstack/localstack:4.14.0")
            .WithNetwork(network)
            .WithNetworkAliases("localstack")
            .WithEnvironment("SERVICES", "sqs,secretsmanager,ssm,kms,iam")
            .WithEnvironment("AWS_DEFAULT_REGION", LocalStackRegion)
            .WithEnvironment("DEFAULT_REGION", LocalStackRegion)
            .WithPortBinding(4566, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(4566)
                .ForPath("/_localstack/health")))
            .WithCleanUp(true)
            .Build();

        await localStack.StartAsync();

        terraformRunner = new ContainerBuilder()
            .WithImage("hashicorp/terraform:1.9")
            .WithNetwork(network)
            .WithEntrypoint("sh", "-c", "sleep 900")
            .WithBindMount(Path.Combine(repositoryRootForTerraform, "infra", "terraform"), "/workspace/infra/terraform", AccessMode.ReadOnly)
            .WithEnvironment("AWS_ACCESS_KEY_ID", LocalStackAccessKey)
            .WithEnvironment("AWS_SECRET_ACCESS_KEY", LocalStackSecretKey)
            .WithEnvironment("AWS_DEFAULT_REGION", LocalStackRegion)
            .WithEnvironment("TF_VAR_localstack_endpoint", "http://localstack:4566")
            .WithCleanUp(true)
            .Build();

        await terraformRunner.StartAsync();

        // Copia para um diretório gravável dentro do container - nunca escreve
        // .terraform/terraform.tfstate na árvore real do repositório no host
        // (mesma técnica de LocalStackSecurityFixture/terraform-provisioner).
        // "rm -rf .../environments/*/.terraform" apos a copia: "cp -R" copia
        // symlinks COMO symlinks - se o host usou
        // scripts/ci/terraform-cached.sh (cache compartilhado via
        // TF_PLUGIN_CACHE_DIR), ".terraform/providers/.../linux_amd64" no
        // host pode ser um symlink para um caminho absoluto que so existe
        // DENTRO de outro container - copiado aqui, vira um symlink
        // quebrado e "terraform init" recusa o pacote (reproduzido e
        // corrigido na mesma auditoria que corrigiu LocalStackSecurityFixture.cs).
        await ExecTerraformOrThrowAsync(
            "rm -rf /tmp/terraform && cp -R /workspace/infra/terraform /tmp/terraform && rm -rf /tmp/terraform/environments/*/.terraform",
            "preparar diretório terraform temporário");
        await ExecTerraformOrThrowAsync($"cd {TerraformWorkDir} && terraform init -input=false", "terraform init");
        await ExecTerraformOrThrowAsync($"cd {TerraformWorkDir} && terraform apply -input=false -auto-approve", "terraform apply");

        // WithTmpfsMount evita o volume anônimo que a imagem postgres declara
        // para /var/lib/postgresql/data - ver comentário completo em
        // LedgerIntegrationCollection.cs.
        ledgerPostgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("ledger")
            .WithUsername("ledger")
            .WithPassword("ledger")
            .WithNetwork(network)
            .WithNetworkAliases("ledger-postgres")
            .WithTmpfsMount("/var/lib/postgresql/data")
            .WithCleanUp(true)
            .Build();

        consolidationPostgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("consolidation")
            .WithUsername("consolidation")
            .WithPassword("consolidation")
            .WithNetwork(network)
            .WithNetworkAliases("consolidation-postgres")
            .WithTmpfsMount("/var/lib/postgresql/data")
            .WithCleanUp(true)
            .Build();

        await Task.WhenAll(ledgerPostgres.StartAsync(), consolidationPostgres.StartAsync());
        await MigrateAsync();

        var leaf = certificateAuthority.IssueLeafCertificate("keycloak.localhost", "localhost", "127.0.0.1");
        var repositoryRoot = LocateRepositoryRoot();
        var realmImportBytes = await File.ReadAllBytesAsync(
            Path.Combine(repositoryRoot, "infra", "keycloak", "realm", "banco-carrefour-realm.json"));

        keycloak = new ContainerBuilder()
            .WithImage(KeycloakImage)
            .WithNetwork(network)
            .WithNetworkAliases("keycloak")
            .WithCommand("start-dev", "--import-realm")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_CLIENT_ID", bootstrapAdminClientId)
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_CLIENT_SECRET", bootstrapAdminClientSecret)
            .WithEnvironment("KC_HOSTNAME", "https://keycloak.localhost:8443")
            .WithEnvironment("KC_HOSTNAME_STRICT", "false")
            .WithEnvironment("KC_HEALTH_ENABLED", "true")
            .WithResourceMapping(realmImportBytes, "/opt/keycloak/data/import/banco-carrefour-realm.json")
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(8080))
            .WithCleanUp(true)
            .Build();

        await keycloak.StartAsync();

        // Cliente administrativo direto (sem passar pelo edge-proxy/WAF): usado
        // só para reconfigurar o mapper de audience do Keycloak entre os
        // marcos da prova de restart do SSM. O WAF real (OWASP CRS) bloqueia
        // PUT no path /admin/ por padrão - correto para o tráfego de produto,
        // mas isso é administração de teste, não o fluxo de autenticação sob
        // teste (que continua indo pelo edge-proxy real em todos os outros
        // métodos desta fixture).
        directKeycloakClient = new HttpClient();
        directAdminClient = new KeycloakAdminClient(
            directKeycloakClient,
            $"http://127.0.0.1:{keycloak.GetMappedPublicPort(8080)}",
            KeycloakTestRealm.RealmName);

        // As duas APIs precisam existir e estar com o alias de rede
        // registrado ANTES do edge-proxy subir: o template real usa
        // proxy_pass estático (não variável), então o nginx resolve
        // "ledger-api"/"consolidation-api" uma única vez, na inicialização
        // — se esses hosts ainda não existirem na rede nesse momento, o
        // nginx falha ao carregar a config e o container do edge-proxy
        // encerra imediatamente (confirmado por execução real).
        var caCertBytes = Encoding.UTF8.GetBytes(certificateAuthority.CaCertificate.ExportCertificatePem());

        ledgerApi = new ContainerBuilder()
            .WithImage(LedgerApiImage)
            .WithNetwork(network)
            .WithNetworkAliases("ledger-api")
            .WithEnvironment("ASPNETCORE_URLS", "http://0.0.0.0:8080")
            // Environment "Testing": único ambiente em que EnvironmentGuard
            // permite Enabled=false em SecretsManager (ver ADR-0009/ADR-0009 e
            // EnvironmentGuard em Ledger.Infrastructure). SystemsManager
            // permanece habilitado (Enabled=true, o padrão) - issuer/audience
            // vêm do SSM real provisionado acima pelo Terraform real, nunca de
            // Authentication__Authority/Audience (ADR-0009).
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Testing")
            .WithEnvironment("SystemsManager__ServiceUrl", "http://localstack:4566")
            .WithEnvironment("SystemsManager__Region", LocalStackRegion)
            .WithEnvironment("SystemsManager__AccessKey", LocalStackAccessKey)
            .WithEnvironment("SystemsManager__SecretKey", LocalStackSecretKey)
            .WithEnvironment("SystemsManager__IssuerParameterName", "/banco-carrefour/oidc/issuer")
            .WithEnvironment("SystemsManager__AudienceParameterName", "/banco-carrefour/oidc/ledger-audience")
            .WithEnvironment("ConnectionStrings__Ledger", "Host=ledger-postgres;Port=5432;Database=ledger;Username=ledger;Password=ledger")
            // Bypass explícito do Secrets Manager (ADR-0009): esta fixture não
            // provisiona os secrets de credencial de banco (isso já é coberto
            // por LocalStackSecurityTests); a connection string acima já vem
            // completa.
            .WithEnvironment("SecretsManager__Enabled", "false")
            .WithResourceMapping(caCertBytes, "/run/secrets/ca/ca.crt")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(8080))
            .WithCleanUp(true)
            .Build();

        consolidationApi = new ContainerBuilder()
            .WithImage(ConsolidationApiImage)
            .WithNetwork(network)
            .WithNetworkAliases("consolidation-api")
            .WithEnvironment("ASPNETCORE_URLS", "http://0.0.0.0:8080")
            // Environment "Testing"/SystemsManager real: ver comentário
            // equivalente no ledgerApi acima.
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Testing")
            .WithEnvironment("SystemsManager__ServiceUrl", "http://localstack:4566")
            .WithEnvironment("SystemsManager__Region", LocalStackRegion)
            .WithEnvironment("SystemsManager__AccessKey", LocalStackAccessKey)
            .WithEnvironment("SystemsManager__SecretKey", LocalStackSecretKey)
            .WithEnvironment("SystemsManager__IssuerParameterName", "/banco-carrefour/oidc/issuer")
            .WithEnvironment("SystemsManager__AudienceParameterName", "/banco-carrefour/oidc/consolidation-audience")
            .WithEnvironment("ConnectionStrings__Consolidation", "Host=consolidation-postgres;Port=5432;Database=consolidation;Username=consolidation;Password=consolidation")
            // Bypass explícito do Secrets Manager (ADR-0009): ver comentário
            // equivalente no ledgerApi acima.
            .WithEnvironment("SecretsManager__Enabled", "false")
            .WithResourceMapping(caCertBytes, "/run/secrets/ca/ca.crt")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(8080))
            .WithCleanUp(true)
            .Build();

        await Task.WhenAll(ledgerApi.StartAsync(), consolidationApi.StartAsync());

        var trustedHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = certificateAuthority.ValidateServerCertificate
        };
        edgeClient = new HttpClient(trustedHandler);
        keycloakViaEdgeClient = new HttpClient(new HostRewritingHandler(KeycloakHostHeader, new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = certificateAuthority.ValidateServerCertificate
        }));

        const UnixFileModes ReadOnlyFileMode = UnixFileModes.UserRead | UnixFileModes.UserWrite
            | UnixFileModes.GroupRead | UnixFileModes.OtherRead;
        const UnixFileModes ExecutableFileMode = ReadOnlyFileMode
            | UnixFileModes.UserExecute | UnixFileModes.GroupExecute | UnixFileModes.OtherExecute;

        var nginxTemplateBytes = await File.ReadAllBytesAsync(
            Path.Combine(repositoryRoot, "infra", "edge-proxy", "default.conf.template"));
        var stageTlsKeyScriptBytes = await File.ReadAllBytesAsync(
            Path.Combine(repositoryRoot, "infra", "edge-proxy", "95-stage-tls-key.sh"));

        edgeProxy = new ContainerBuilder()
            .WithImage(EdgeProxyImage)
            .WithNetwork(network)
            .WithNetworkAliases("keycloak.localhost")
            .WithEnvironment("MODSEC_RULE_ENGINE", "on")
            .WithEnvironment("PORT", "8080")
            .WithEnvironment("SSL_PORT", "8443")
            .WithEnvironment("SERVER_NAME", "localhost")
            .WithEnvironment("SSL_CERT_KEY_FILE", "/etc/nginx/server.key")
            .WithResourceMapping(nginxTemplateBytes, "/etc/nginx/templates/conf.d/default.conf.template", ReadOnlyFileMode)
            .WithResourceMapping(stageTlsKeyScriptBytes, "/docker-entrypoint.d/95-stage-tls-key.sh", ExecutableFileMode)
            .WithResourceMapping(Encoding.UTF8.GetBytes(leaf.CertificatePem), "/etc/nginx/conf/server.crt", ReadOnlyFileMode)
            .WithResourceMapping(Encoding.UTF8.GetBytes(leaf.PrivateKeyPem), "/run/edge-secrets/server.key", ReadOnlyFileMode)
            .WithPortBinding(8443, true)
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(8443))
            .WithCleanUp(true)
            .Build();

        await edgeProxy.StartAsync();

        adminClient = new KeycloakAdminClient(keycloakViaEdgeClient, EdgeHttpsBaseUrl, KeycloakTestRealm.RealmName);

        await WaitForEdgeReadyAsync();

        var adminToken = await adminClient.GetTokenAsync(bootstrapAdminClientId, bootstrapAdminClientSecret, realmOverride: "master");
        merchantAClientSecret = await adminClient.GetOrCreateClientSecretAsync(adminToken, KeycloakTestRealm.MerchantAClientId);
    }

    private async Task WaitForEdgeReadyAsync()
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{EdgeHttpsBaseUrl}/ledger/health/ready");
                request.Headers.Host = "localhost:8443";
                using var response = await edgeClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // ainda subindo — tenta de novo
            }

            await Task.Delay(2000);
        }

        throw new TimeoutException("edge-proxy/Ledger.Api não ficaram saudáveis a tempo.");
    }

    private async Task MigrateAsync()
    {
        var ledgerOptions = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseNpgsql(ledgerPostgres.GetConnectionString())
            .Options;
        await using (var ledgerContext = new LedgerDbContext(ledgerOptions))
        {
            await ledgerContext.Database.MigrateAsync();
        }

        var consolidationOptions = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(consolidationPostgres.GetConnectionString())
            .Options;
        await using var consolidationContext = new ConsolidationDbContext(consolidationOptions);
        await consolidationContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        edgeClient?.Dispose();
        keycloakViaEdgeClient?.Dispose();
        directKeycloakClient?.Dispose();

        await SafelyAsync(() => edgeProxy is null ? Task.CompletedTask : edgeProxy.DisposeAsync().AsTask());
        await SafelyAsync(() => ledgerApi is null ? Task.CompletedTask : ledgerApi.DisposeAsync().AsTask());
        await SafelyAsync(() => consolidationApi is null ? Task.CompletedTask : consolidationApi.DisposeAsync().AsTask());
        await SafelyAsync(() => keycloak is null ? Task.CompletedTask : keycloak.DisposeAsync().AsTask());
        await SafelyAsync(() => ledgerPostgres is null ? Task.CompletedTask : ledgerPostgres.DisposeAsync().AsTask());
        await SafelyAsync(() => consolidationPostgres is null ? Task.CompletedTask : consolidationPostgres.DisposeAsync().AsTask());
        await SafelyAsync(async () =>
        {
            if (terraformRunner is not null)
            {
                await ExecTerraformOrThrowAsync(
                    $"cd {TerraformWorkDir} && terraform destroy -input=false -auto-approve", "terraform destroy");
            }
        });
        await SafelyAsync(() => terraformRunner is null ? Task.CompletedTask : terraformRunner.DisposeAsync().AsTask());
        await SafelyAsync(() => localStack is null ? Task.CompletedTask : localStack.DisposeAsync().AsTask());
        await SafelyAsync(() => network is null ? Task.CompletedTask : network.DeleteAsync());
        certificateAuthority.Dispose();
    }

    private async Task ExecTerraformOrThrowAsync(string shellCommand, string step)
    {
        var result = await terraformRunner.ExecAsync(["sh", "-c", shellCommand]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Falha em '{step}' (exit {result.ExitCode}): {result.Stderr}");
        }
    }

    private static async Task SafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BancoCarrefour.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Não foi possível localizar a raiz do repositório.");
    }
}

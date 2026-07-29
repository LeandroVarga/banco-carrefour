using System.Security.Cryptography.X509Certificates;
using BancoCarrefour.Consolidation.Infrastructure;
using BancoCarrefour.Ledger.Infrastructure;
using BancoCarrefour.Security.IntegrationTests.Shared;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.Identity;

[CollectionDefinition(Name)]
public sealed class IdentitySecurityCollection : ICollectionFixture<IdentityFixture>
{
    public const string Name = "IdentitySecurity";
}

/// <summary>
/// Keycloak real (mesma imagem/digest fixados em docker-compose.yml),
/// modo <c>start-dev</c> (rápido — decisão deliberada, documentada no plano
///, diferente da configuração `start` real do Compose), com o
/// realm real (<c>infra/keycloak/realm/banco-carrefour-realm.json</c>)
/// importado sem alteração de comportamento — só clients adicionais de
/// teste foram acrescentados a esse arquivo para cobrir casos de borda que
/// o Keycloak não emitiria naturalmente com os clients de produção
/// (ver <see cref="KeycloakTestRealm"/>). HTTPS real via certificado
/// efêmero (nunca os certificados de <c>.local/security/</c>) — necessário
/// porque <c>RequireHttpsMetadata=true</c> nunca é relaxado para os testes.
/// </summary>
public sealed class IdentityFixture : IAsyncLifetime
{
    // Mesma versão exata fixada em docker-compose.yml (quay.io/keycloak/keycloak:26.7.0,
    // digest sha256:0f198be292568439d700cdbfb893e69a6009bb43a94a06a945b1d3d506c76b13).
    // Testcontainers 3.10.0 não aceita o formato combinado "tag@digest" em
    // WithImage(string) (DotNet.Testcontainers.Images.MatchImage rejeita —
    // confirmado por execução real) — a imagem já cacheada localmente sob
    // esta tag é a mesma resolvida pelo digest acima.
    private const string KeycloakImage = "quay.io/keycloak/keycloak:26.7.0";

    private readonly EphemeralCertificateAuthority certificateAuthority = new();
    private readonly string bootstrapAdminClientId = $"bootstrap-{Guid.NewGuid():N}";
    private readonly string bootstrapAdminClientSecret = $"{Guid.NewGuid():N}{Guid.NewGuid():N}";

    // WithTmpfsMount evita o volume anônimo que a imagem postgres declara
    // para /var/lib/postgresql/data - ver comentário completo em
    // LedgerIntegrationCollection.cs.
    private readonly PostgreSqlContainer ledgerPostgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("ledger")
        .WithUsername("ledger")
        .WithPassword("ledger")
        .WithTmpfsMount("/var/lib/postgresql/data")
        .WithCleanUp(true)
        .Build();

    private readonly PostgreSqlContainer consolidationPostgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("consolidation")
        .WithUsername("consolidation")
        .WithPassword("consolidation")
        .WithTmpfsMount("/var/lib/postgresql/data")
        .WithCleanUp(true)
        .Build();

    private IContainer keycloak = null!;
    private HttpClient trustedHttpClient = null!;
    private KeycloakAdminClient adminClient = null!;

    public string Authority => $"https://127.0.0.1:{keycloak.GetMappedPublicPort(8443)}/realms/{KeycloakTestRealm.RealmName}";

    public string BaseUrl => $"https://127.0.0.1:{keycloak.GetMappedPublicPort(8443)}";

    public X509Certificate2 CaCertificate => certificateAuthority.CaCertificate;

    private readonly Dictionary<string, string> clientSecrets = new(StringComparer.Ordinal);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(ledgerPostgres.StartAsync(), consolidationPostgres.StartAsync());
        await MigrateAsync();

        var leaf = certificateAuthority.IssueLeafCertificate("127.0.0.1", "localhost");
        var realmImportPath = Path.Combine(RepositoryRoot, "infra", "keycloak", "realm", "banco-carrefour-realm.json");
        var realmImportBytes = await File.ReadAllBytesAsync(realmImportPath);

        keycloak = new ContainerBuilder()
            .WithImage(KeycloakImage)
            .WithCommand("start-dev", "--import-realm")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_CLIENT_ID", bootstrapAdminClientId)
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_CLIENT_SECRET", bootstrapAdminClientSecret)
            .WithEnvironment("KC_HTTPS_CERTIFICATE_FILE", "/opt/keycloak/conf/server.crt.pem")
            .WithEnvironment("KC_HTTPS_CERTIFICATE_KEY_FILE", "/opt/keycloak/conf/server.key.pem")
            .WithEnvironment("KC_HEALTH_ENABLED", "true")
            .WithResourceMapping(realmImportBytes, "/opt/keycloak/data/import/banco-carrefour-realm.json")
            .WithResourceMapping(
                System.Text.Encoding.UTF8.GetBytes(leaf.CertificatePem),
                "/opt/keycloak/conf/server.crt.pem")
            .WithResourceMapping(
                System.Text.Encoding.UTF8.GetBytes(leaf.PrivateKeyPem),
                "/opt/keycloak/conf/server.key.pem")
            .WithPortBinding(8443, true)
            .WithPortBinding(9000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(9000))
            .WithCleanUp(true)
            .Build();

        await keycloak.StartAsync();

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = certificateAuthority.ValidateServerCertificate
        };
        trustedHttpClient = new HttpClient(handler);
        adminClient = new KeycloakAdminClient(trustedHttpClient, BaseUrl, KeycloakTestRealm.RealmName);

        // O painel de gestão (porta 9000) sobe em HTTPS quando o app também
        // usa HTTPS (mesmo certificado) — por isso o wait strategy acima só
        // confirma a porta TCP; a prontidão real é verificada aqui via
        // HTTPS com a CA efêmera do teste, contra o endpoint real de
        // health, com poll manual (a API HTTP-only de Wait não serve aqui).
        await WaitForKeycloakReadyAsync();

        var adminToken = await adminClient.GetTokenAsync(
            bootstrapAdminClientId,
            bootstrapAdminClientSecret,
            realmOverride: "master");

        foreach (var clientId in new[]
                 {
                     KeycloakTestRealm.MerchantAClientId,
                     KeycloakTestRealm.MerchantBClientId,
                     KeycloakTestRealm.MerchantAShortLivedClientId,
                     KeycloakTestRealm.MerchantMissingClientId,
                     KeycloakTestRealm.MerchantBlankClientId,
                     KeycloakTestRealm.MerchantOverflowClientId,
                     KeycloakTestRealm.MerchantAAudienceOnlyClientId
                 })
        {
            clientSecrets[clientId] = await adminClient.GetOrCreateClientSecretAsync(adminToken, clientId);
        }
    }

    private async Task WaitForKeycloakReadyAsync()
    {
        var healthUrl = $"https://127.0.0.1:{keycloak.GetMappedPublicPort(9000)}/health/ready";

        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var response = await trustedHttpClient.GetAsync(healthUrl);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Ainda subindo — tenta de novo até o limite de tentativas.
            }

            await Task.Delay(2000);
        }

        throw new TimeoutException($"Keycloak não ficou saudável a tempo em {healthUrl}.");
    }

    public async Task<string> IssueTokenAsync(string clientId, string? scope = null, string? hostHeaderOverride = null)
    {
        var secret = clientSecrets[clientId];
        return await adminClient.GetTokenAsync(clientId, secret, scope, hostHeaderOverride);
    }

    public async Task<System.Net.HttpStatusCode> RequestTokenStatusCodeAsync(string clientId, string scope)
    {
        var secret = clientSecrets[clientId];
        return await adminClient.RequestTokenStatusCodeAsync(clientId, secret, scope);
    }

    public HttpClientHandler CreateTrustedHandler()
    {
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = certificateAuthority.ValidateServerCertificate
        };
    }

    internal LedgerIdentityApiFactory CreateLedgerApiFactory()
    {
        return new LedgerIdentityApiFactory(ledgerPostgres.GetConnectionString(), Authority, certificateAuthority);
    }

    internal ConsolidationIdentityApiFactory CreateConsolidationApiFactory()
    {
        return new ConsolidationIdentityApiFactory(consolidationPostgres.GetConnectionString(), Authority, certificateAuthority);
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
        trustedHttpClient.Dispose();
        await keycloak.DisposeAsync();
        await ledgerPostgres.DisposeAsync();
        await consolidationPostgres.DisposeAsync();
        certificateAuthority.Dispose();
    }

    private static string RepositoryRoot { get; } = LocateRepositoryRoot();

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

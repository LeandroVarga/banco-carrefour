using BancoCarrefour.Ledger.Api;
using BancoCarrefour.Ledger.Infrastructure;
using BancoCarrefour.Security.IntegrationTests.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BancoCarrefour.Security.IntegrationTests.Identity;

/// <summary>
/// Hospeda o Ledger.Api real, com o pipeline de autenticação de produção
/// intacto (<see cref="Ledger.Api.Authentication.LedgerAuthentication"/> não
/// é tocado nem substituído): <c>Authority</c> aponta para o Keycloak real
/// da <see cref="IdentityFixture"/>, <c>RequireHttpsMetadata</c> continua
/// <c>true</c> e a validação de assinatura/issuer/audience continua via
/// JWKS real. A ÚNICA adaptação de teste é o <c>Backchannel</c> do
/// JwtBearer (o HttpClient que o middleware usa para buscar discovery/JWKS),
/// que passa a confiar na CA efêmera do teste — equivalente ao
/// <c>update-ca-certificates</c> do entrypoint real
/// (<c>docker/api-entrypoint.sh</c>), nunca um "aceitar qualquer
/// certificado".
/// </summary>
internal sealed class LedgerIdentityApiFactory(
    string connectionString,
    string authority,
    EphemeralCertificateAuthority certificateAuthority)
    : WebApplicationFactory<Program>
{
    public string ConnectionString { get; } = connectionString;

    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

        await dbContext.OutboxMessages.ExecuteDeleteAsync();
        await dbContext.InputIdempotencyRecords.ExecuteDeleteAsync();
        await dbContext.Entries.ExecuteDeleteAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Environment "Testing": ver comentário equivalente em LedgerApiFactory.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Ledger", ConnectionString);
        // UseSetting (não só ConfigureAppConfiguration): ver comentário
        // equivalente em LedgerApiFactory - confirmado por execução real.
        builder.UseSetting("SecretsManager:Enabled", "false");
        builder.UseSetting("SystemsManager:Enabled", "false");
        builder.UseSetting("Authentication:Authority", authority);
        builder.UseSetting("Authentication:Audience", KeycloakTestRealm.LedgerAudience);
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Ledger"] = ConnectionString,
                ["Authentication:Authority"] = authority,
                ["Authentication:Audience"] = KeycloakTestRealm.LedgerAudience,
                // Bypass explícito do Secrets Manager/SSM (ADR-0009/ADR-0009):
                // este teste já fornece ConnectionStrings:Ledger completa
                // (Testcontainers) e Authentication:Authority/Audience (Keycloak
                // real efêmero desta fixture).
                ["SecretsManager:Enabled"] = "false",
                ["SystemsManager:Enabled"] = "false"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Authority/Backchannel precisam ser fixados em Configure (fase
            // anterior a qualquer PostConfigure): o próprio pacote JwtBearer
            // registra um IPostConfigureOptions interno que constrói o
            // ConfigurationManager (usado para buscar JWKS) a partir de
            // options.Backchannel/Authority — se essa troca acontecesse em
            // PostConfigure, o ConfigurationManager já teria capturado o
            // HttpClient padrão (sem confiar na CA efêmera), causando
            // IDX10500 (nenhuma chave de assinatura encontrada), confirmado
            // por execução real.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = authority;
                options.Backchannel = new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = certificateAuthority.ValidateServerCertificate
                });
            });

            // ValidIssuer/ValidAudience, por outro lado, precisam ser
            // fixados em PostConfigure (garantidamente por último): a
            // leitura de Authentication:Authority feita dentro do Configure
            // de produção (LedgerAuthentication.AddLedgerAuthentication) não
            // refletia de forma confiável o valor injetado via
            // ConfigureAppConfiguration acima (raiz observada por execução
            // real: ValidIssuer permanecia no DefaultAuthority hardcoded).
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.ValidIssuer = authority;
                options.TokenValidationParameters.ValidAudience = KeycloakTestRealm.LedgerAudience;
            });
        });
    }
}

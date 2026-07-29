extern alias ConsolidationApiAssembly;

using BancoCarrefour.Consolidation.Infrastructure;
using BancoCarrefour.Security.IntegrationTests.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ConsolidationProgram = ConsolidationApiAssembly::Program;

namespace BancoCarrefour.Security.IntegrationTests.Identity;

/// <summary>Mesma abordagem de <see cref="LedgerIdentityApiFactory"/>, para o Consolidation.Api real.</summary>
internal sealed class ConsolidationIdentityApiFactory(
    string connectionString,
    string authority,
    EphemeralCertificateAuthority certificateAuthority)
    : WebApplicationFactory<ConsolidationProgram>
{
    public string ConnectionString { get; } = connectionString;

    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();

        await dbContext.ProcessedEvents.ExecuteDeleteAsync();
        await dbContext.DailyBalances.ExecuteDeleteAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Environment "Testing": ver comentário equivalente em LedgerApiFactory.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Consolidation", ConnectionString);
        // UseSetting (não só ConfigureAppConfiguration): ver comentário
        // equivalente em LedgerApiFactory - confirmado por execução real.
        builder.UseSetting("SecretsManager:Enabled", "false");
        builder.UseSetting("SystemsManager:Enabled", "false");
        builder.UseSetting("Authentication:Authority", authority);
        builder.UseSetting("Authentication:Audience", KeycloakTestRealm.ConsolidationAudience);
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Consolidation"] = ConnectionString,
                ["Authentication:Authority"] = authority,
                ["Authentication:Audience"] = KeycloakTestRealm.ConsolidationAudience,
                // Bypass explícito do Secrets Manager/SSM (ADR-0009/ADR-0009):
                // este teste já fornece ConnectionStrings:Consolidation completa
                // (Testcontainers) e Authentication:Authority/Audience (Keycloak
                // real efêmero desta fixture).
                ["SecretsManager:Enabled"] = "false",
                ["SystemsManager:Enabled"] = "false"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Ver comentário equivalente em LedgerIdentityApiFactory.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = authority;
                options.Backchannel = new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = certificateAuthority.ValidateServerCertificate
                });
            });

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.ValidIssuer = authority;
                options.TokenValidationParameters.ValidAudience = KeycloakTestRealm.ConsolidationAudience;
            });
        });
    }
}

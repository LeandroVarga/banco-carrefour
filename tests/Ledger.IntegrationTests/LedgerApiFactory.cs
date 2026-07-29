using BancoCarrefour.Ledger.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace BancoCarrefour.Ledger.IntegrationTests;

public sealed class LedgerApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string Issuer = "https://test-issuer.local/realms/banco-carrefour";
    public const string Audience = "ledger-api";

    // Chave RSA exclusiva deste host de teste (RS256 real, nunca HS256):
    // substitui a resolução via Authority/JWKS por uma chave estática local,
    // só para testes que não avaliam segurança em si. O runtime produtivo
    // (LedgerAuthentication.cs) nunca referencia esta chave.
    public static readonly RsaSecurityKey TestSigningKey = new(RSA.Create(2048));

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
        // Environment "Testing" (via UseSetting, mesmo motivo abaixo): é o
        // único ambiente em que EnvironmentGuard permite Enabled=false em
        // SecretsManager/SystemsManager (ADR-0009/ADR-0009).
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Ledger", ConnectionString);
        // UseSetting (não só ConfigureAppConfiguration): o WebApplicationFactory
        // executa Program.cs uma primeira vez via HostFactoryResolver antes de
        // aplicar os overrides de ConfigureAppConfiguration - só valores via
        // UseSetting já estão disponíveis nessa passagem inicial (confirmado por
        // execução real: LedgerConnectionStringResolver.Resolve rodava antes do
        // bypass ser aplicado e falhava com "SecretName não configurado"; o
        // mesmo valia para LedgerOidcConfigurationResolver e
        // Authentication:Authority/Audience, que também precisam de UseSetting).
        builder.UseSetting("SecretsManager:Enabled", "false");
        builder.UseSetting("SystemsManager:Enabled", "false");
        builder.UseSetting("Authentication:Authority", Issuer);
        builder.UseSetting("Authentication:Audience", Audience);
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Ledger"] = ConnectionString,
                ["Authentication:Authority"] = Issuer,
                ["Authentication:Audience"] = Audience,
                // Bypass explícito do Secrets Manager/SSM (ADR-0009/ADR-0009):
                // este teste já fornece ConnectionStrings:Ledger completa
                // (Testcontainers) e Authentication:Authority/Audience.
                ["SecretsManager:Enabled"] = "false",
                ["SystemsManager:Enabled"] = "false"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.MetadataAddress = string.Empty;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                options.TokenValidationParameters.IssuerSigningKey = TestSigningKey;
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.ValidAudience = Audience;
            });
        });
    }
}

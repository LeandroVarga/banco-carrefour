using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace BancoCarrefour.Ledger.Api.Authentication;

internal static class LedgerAuthentication
{
    public const string MerchantPolicy = "MerchantAuthenticated";
    public const string MerchantClaim = "merchant_id";
    public const int MerchantIdMaxLength = 64;

    public static IServiceCollection AddLedgerAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var signingKey = configuration["Authentication:SigningKey"]
            ?? "ledger-local-development-signing-key-32-bytes";

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(MerchantPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                {
                    var merchantId = context.User.FindFirst(MerchantClaim)?.Value;

                    return !string.IsNullOrWhiteSpace(merchantId)
                        && merchantId.Length <= MerchantIdMaxLength;
                });
            });

        return services;
    }
}

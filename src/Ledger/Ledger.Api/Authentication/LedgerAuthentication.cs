using BancoCarrefour.Ledger.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace BancoCarrefour.Ledger.Api.Authentication;

internal static class LedgerAuthentication
{
    public const string MerchantPolicy = "MerchantAuthenticated";
    public const string MerchantClaim = "merchant_id";
    public const int MerchantIdMaxLength = 64;
    private const string DefaultIssuer = "banco-carrefour-local";
    private const string DefaultAudience = "banco-carrefour-api";

    public static IServiceCollection AddLedgerAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var signingKey = configuration["Authentication:SigningKey"]
            ?? "ledger-local-development-signing-key-32-bytes";
        var issuer = configuration["Authentication:Issuer"] ?? DefaultIssuer;
        var audience = configuration["Authentication:Audience"] ?? DefaultAudience;

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                options.Events = new JwtBearerEvents
                {
                    OnChallenge = context =>
                    {
                        context.HandleResponse();

                        return ApiErrorResponses.WriteAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            "AUTHENTICATION_ERROR",
                            "Não autenticado.");
                    },
                    OnForbidden = context =>
                    {
                        return ApiErrorResponses.WriteAsync(
                            context.HttpContext,
                            StatusCodes.Status403Forbidden,
                            "AUTHORIZATION_ERROR",
                            "Não autorizado.");
                    }
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

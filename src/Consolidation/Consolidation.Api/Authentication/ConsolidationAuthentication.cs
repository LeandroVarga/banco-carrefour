using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Linq;
using System.Security.Claims;

namespace BancoCarrefour.Consolidation.Api.Authentication;

internal static class ConsolidationAuthentication
{
    public const string MerchantPolicy = "MerchantAuthenticated";
    public const string ConsolidationReadScopePolicy = "ConsolidationReadScope";
    public const string MerchantClaim = "merchant_id";
    public const string ScopeClaim = "scope";
    public const string ConsolidationReadScope = "consolidation.read";
    public const int MerchantIdMaxLength = 64;

    /// <summary>
    /// Recebe authority/audience já resolvidos (SSM em runtime oficial, ou o
    /// bypass explícito de Testing — ver
    /// <c>BancoCarrefour.Consolidation.Infrastructure.Ssm.ConsolidationOidcConfigurationResolver</c>,
    /// ADR-0009) em vez de ler <c>IConfiguration</c> diretamente: evita uma
    /// segunda fonte concorrente de issuer/audience dentro deste método.
    /// </summary>
    public static IServiceCollection AddConsolidationAuthentication(
        this IServiceCollection services,
        string authority,
        string audience)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.RequireHttpsMetadata = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
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
                policy.RequireAssertion(context => HasValidMerchantId(context.User));
            })
            .AddPolicy(ConsolidationReadScopePolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => HasScope(context.User, ConsolidationReadScope));
            });

        return services;
    }

    private static bool HasValidMerchantId(ClaimsPrincipal user)
    {
        var merchantId = user.FindFirst(MerchantClaim)?.Value;

        return !string.IsNullOrWhiteSpace(merchantId) && merchantId.Length <= MerchantIdMaxLength;
    }

    private static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        var scopeClaim = user.FindFirst(ScopeClaim)?.Value;

        if (string.IsNullOrWhiteSpace(scopeClaim))
        {
            return false;
        }

        return scopeClaim
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(requiredScope, StringComparer.Ordinal);
    }
}

using BancoCarrefour.Consolidation.Api.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace BancoCarrefour.Consolidation.Api;

internal static class BusinessRateLimiting
{
    public const string PolicyName = "business-endpoints";
    private const int DefaultPermitLimit = 6_000;
    private const int DefaultWindowSeconds = 60;

    public static IServiceCollection AddBusinessRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = LoadOptions(configuration);

        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiterOptions.OnRejected = (context, _) =>
            {
                return new ValueTask(ApiErrorResponses.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    "RATE_LIMIT_EXCEEDED",
                    "Limite de requisições excedido."));
            };

            rateLimiterOptions.AddPolicy(PolicyName, httpContext =>
            {
                var partitionKey = ResolvePartitionKey(httpContext);

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = options.PermitLimit,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        Window = options.Window
                    });
            });
        });

        return services;
    }

    private static BusinessRateLimitOptions LoadOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("RateLimit");
        var permitLimit = ReadPositiveInt(section, "PermitLimit", DefaultPermitLimit);
        var windowSeconds = ReadPositiveInt(section, "WindowSeconds", DefaultWindowSeconds);

        return new BusinessRateLimitOptions(
            permitLimit,
            TimeSpan.FromSeconds(windowSeconds));
    }

    private static int ReadPositiveInt(IConfiguration configuration, string key, int defaultValue)
    {
        return int.TryParse(configuration[key], out var value) && value > 0
            ? value
            : defaultValue;
    }

    private static string ResolvePartitionKey(HttpContext httpContext)
    {
        var merchantId = httpContext.User.FindFirstValue(ConsolidationAuthentication.MerchantClaim);

        if (!string.IsNullOrWhiteSpace(merchantId))
        {
            return $"merchant:{merchantId}";
        }

        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();

        return string.IsNullOrWhiteSpace(remoteIp)
            ? "anonymous"
            : $"ip:{remoteIp}";
    }

    private sealed record BusinessRateLimitOptions(int PermitLimit, TimeSpan Window);
}

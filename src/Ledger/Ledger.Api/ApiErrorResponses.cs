using BancoCarrefour.Ledger.Api.Entries;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;

namespace BancoCarrefour.Ledger.Api;

internal static class ApiErrorResponses
{
    public const int CorrelationIdMaxLength = 128;
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ResolveCorrelationId(HttpContext httpContext)
    {
        var correlationId = httpContext.Request.Headers[CorrelationIdHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > CorrelationIdMaxLength)
        {
            return TrimToCorrelationIdLimit(httpContext.TraceIdentifier);
        }

        return correlationId;
    }

    public static bool HasInvalidCorrelationId(HttpContext httpContext)
    {
        var correlationId = httpContext.Request.Headers[CorrelationIdHeader].FirstOrDefault();

        return !string.IsNullOrWhiteSpace(correlationId) && correlationId.Length > CorrelationIdMaxLength;
    }

    public static ErrorResponse Create(
        string errorCode,
        string message,
        HttpContext httpContext,
        IReadOnlyCollection<string>? details = null)
    {
        return new ErrorResponse(errorCode, message, ResolveCorrelationId(httpContext), details);
    }

    public static async Task WriteAsync(
        HttpContext httpContext,
        int statusCode,
        string errorCode,
        string message,
        IReadOnlyCollection<string>? details = null)
    {
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        httpContext.Response.Clear();
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";

        var error = Create(errorCode, message, httpContext, details);
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, error, JsonOptions, httpContext.RequestAborted);
    }

    private static string TrimToCorrelationIdLimit(string value)
    {
        return value.Length <= CorrelationIdMaxLength ? value : value[..CorrelationIdMaxLength];
    }
}

using BancoCarrefour.Consolidation.Api;
using BancoCarrefour.Consolidation.Api.Authentication;
using BancoCarrefour.Consolidation.Api.DailyBalances;
using BancoCarrefour.Consolidation.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.AddConsolidationApiObservability();

builder.Services.AddConsolidationAuthentication(builder.Configuration);
builder.Services.AddBusinessRateLimiting(builder.Configuration);
builder.Services.AddAuthorization();

var consolidationConnectionString = builder.Configuration.GetConnectionString("Consolidation")
    ?? "Host=consolidation-postgres;Port=5432;Database=consolidation;Username=consolidation;Password=consolidation";

builder.Services.AddDbContext<ConsolidationDbContext>(options => options.UseNpgsql(consolidationConnectionString));
builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<ConsolidationDatabaseHealthCheck>("consolidation-postgres", tags: ["ready"]);

var app = builder.Build();

app.Use(async (httpContext, next) =>
{
    try
    {
        await next(httpContext);
    }
    catch (Exception exception) when (IsDatabaseUnavailable(exception))
    {
        var correlationId = ApiErrorResponses.ResolveCorrelationId(httpContext);
        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Observability.ServiceName);

        Observability.DailyBalanceDatabaseUnavailable.Add(1);
        Activity.Current?.SetTag("correlation.id", correlationId);
        Activity.Current?.SetStatus(ActivityStatusCode.Error, "consolidation database unavailable");

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlation_id"] = correlationId
        }))
        {
            logger.LogWarning(exception, "Consolidation Database indisponível durante requisição HTTP.");
        }

        await ApiErrorResponses.WriteAsync(
            httpContext,
            StatusCodes.Status503ServiceUnavailable,
            "SERVICE_UNAVAILABLE",
            "Dependência indisponível.");
    }
    catch (Exception exception)
    {
        var correlationId = ApiErrorResponses.ResolveCorrelationId(httpContext);
        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Observability.ServiceName);

        Activity.Current?.SetTag("correlation.id", correlationId);
        Activity.Current?.SetStatus(ActivityStatusCode.Error, "internal error");

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlation_id"] = correlationId
        }))
        {
            logger.LogError(exception, "Erro interno durante requisição HTTP.");
        }

        await ApiErrorResponses.WriteAsync(
            httpContext,
            StatusCodes.Status500InternalServerError,
            "INTERNAL_ERROR",
            "Erro interno.");
    }
});

app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = WriteHealthResponseAsync
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync
});

app.MapDailyBalanceEndpoints();

app.Run();

static Task WriteHealthResponseAsync(HttpContext httpContext, HealthReport report)
{
    httpContext.Response.ContentType = "application/json";

    var body = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString()
        })
    };

    return JsonSerializer.SerializeAsync(httpContext.Response.Body, body);
}

static bool IsDatabaseUnavailable(Exception exception)
{
    for (var current = exception; current is not null; current = current.InnerException)
    {
        if (current is NpgsqlException and not PostgresException)
        {
            return true;
        }

        if (current is SocketException or TimeoutException)
        {
            return true;
        }
    }

    return false;
}

internal sealed class ConsolidationDatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();

            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}

public partial class Program;

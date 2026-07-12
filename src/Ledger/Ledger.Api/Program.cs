using BancoCarrefour.Ledger.Api;
using BancoCarrefour.Ledger.Api.Authentication;
using BancoCarrefour.Ledger.Api.Entries;
using BancoCarrefour.Ledger.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});

builder.Services.AddLedgerAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

var ledgerConnectionString = builder.Configuration.GetConnectionString("Ledger")
    ?? "Host=ledger-postgres;Port=5432;Database=ledger;Username=ledger;Password=ledger";

builder.Services.AddDbContext<LedgerDbContext>(options => options.UseNpgsql(ledgerConnectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<LedgerDatabaseHealthCheck>("ledger-postgres", tags: ["ready"]);

var app = builder.Build();

app.Use(async (httpContext, next) =>
{
    try
    {
        await next(httpContext);
    }
    catch (BadHttpRequestException)
    {
        await ApiErrorResponses.WriteAsync(
            httpContext,
            StatusCodes.Status400BadRequest,
            "VALIDATION_ERROR",
            "Requisição inválida.");
    }
    catch (Exception exception) when (IsDatabaseUnavailable(exception))
    {
        await ApiErrorResponses.WriteAsync(
            httpContext,
            StatusCodes.Status503ServiceUnavailable,
            "SERVICE_UNAVAILABLE",
            "Dependência indisponível.");
    }
    catch (Exception)
    {
        await ApiErrorResponses.WriteAsync(
            httpContext,
            StatusCodes.Status500InternalServerError,
            "INTERNAL_ERROR",
            "Erro interno.");
    }
});

app.UseAuthentication();
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

app.MapEntryEndpoints();

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

internal sealed class LedgerDatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

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

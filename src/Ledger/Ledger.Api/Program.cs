using BancoCarrefour.Ledger.Api;
using BancoCarrefour.Ledger.Api.Authentication;
using BancoCarrefour.Ledger.Api.Entries;
using BancoCarrefour.Ledger.Application.RegisterFinancialEntry;
using BancoCarrefour.Ledger.Infrastructure;
using BancoCarrefour.Ledger.Infrastructure.FinancialEntries;
using BancoCarrefour.Ledger.Infrastructure.Secrets;
using BancoCarrefour.Ledger.Infrastructure.Ssm;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.AddLedgerApiObservability();

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});

// ThrowOnBadRequest fixado em true independentemente do ambiente: por padrão
// o minimal API só lança BadHttpRequestException para falha de binding do
// body JSON quando IHostEnvironment.IsDevelopment() é verdadeiro - fora
// disso, ele só define o status 400 e retorna, sem corpo (achado por
// execução real ao introduzir o ambiente "Testing" nesta etapa: os testes de
// payload malformado passavam a receber 400 sem corpo). O contrato de erro
// da API (VALIDATION_ERROR/errorCode/message/correlationId, capturado pelo
// middleware abaixo via catch (BadHttpRequestException)) não pode depender
// de ASPNETCORE_ENVIRONMENT.
builder.Services.Configure<RouteHandlerOptions>(options =>
{
    options.ThrowOnBadRequest = true;
});

var oidcConfiguration = await LedgerOidcConfigurationResolver.ResolveAsync(
    builder.Configuration, builder.Environment.EnvironmentName);

builder.Services.AddLedgerAuthentication(oidcConfiguration.Authority, oidcConfiguration.Audience);
builder.Services.AddBusinessRateLimiting(builder.Configuration);
builder.Services.AddAuthorization();

var ledgerConnectionString = LedgerConnectionStringResolver.Resolve(
    builder.Configuration, builder.Environment.EnvironmentName);

builder.Services.AddDbContext<LedgerDbContext>(options => options.UseNpgsql(ledgerConnectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IFinancialEntryRegistrationStore, EfFinancialEntryRegistrationStore>();
builder.Services.AddScoped<IRegisterFinancialEntryUseCase, RegisterFinancialEntryUseCase>();
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
        var correlationId = ApiErrorResponses.ResolveCorrelationId(httpContext);
        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Observability.ServiceName);

        Observability.EntriesDatabaseUnavailable.Add(1);
        Activity.Current?.SetTag("correlation.id", correlationId);
        Activity.Current?.SetStatus(ActivityStatusCode.Error, "ledger database unavailable");

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlation_id"] = correlationId
        }))
        {
            logger.LogWarning(exception, "Ledger Database indisponível durante requisição HTTP.");
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

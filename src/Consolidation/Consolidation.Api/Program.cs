using BancoCarrefour.Consolidation.Api;
using BancoCarrefour.Consolidation.Api.Authentication;
using BancoCarrefour.Consolidation.Api.DailyBalances;
using BancoCarrefour.Consolidation.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Net.Sockets;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddConsolidationAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

var consolidationConnectionString = builder.Configuration.GetConnectionString("Consolidation")
    ?? "Host=consolidation-postgres;Port=5432;Database=consolidation;Username=consolidation;Password=consolidation";

builder.Services.AddDbContext<ConsolidationDbContext>(options => options.UseNpgsql(consolidationConnectionString));

var app = builder.Build();

app.Use(async (httpContext, next) =>
{
    try
    {
        await next(httpContext);
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

app.MapDailyBalanceEndpoints();

app.Run();

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

public partial class Program;

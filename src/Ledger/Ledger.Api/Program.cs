using BancoCarrefour.Ledger.Api;
using BancoCarrefour.Ledger.Api.Authentication;
using BancoCarrefour.Ledger.Api.Entries;
using BancoCarrefour.Ledger.Persistence;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Net.Sockets;
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

app.MapEntryEndpoints();

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

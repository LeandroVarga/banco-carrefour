using BancoCarrefour.Ledger.Api.Authentication;
using BancoCarrefour.Ledger.Api.Entries;
using BancoCarrefour.Ledger.Persistence;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
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

app.UseAuthentication();
app.UseAuthorization();

app.MapEntryEndpoints();

app.Run();

public partial class Program;

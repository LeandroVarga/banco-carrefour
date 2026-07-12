using BancoCarrefour.Consolidation.Application;
using BancoCarrefour.Consolidation.Persistence;
using BancoCarrefour.Consolidation.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

var consolidationConnectionString = builder.Configuration.GetConnectionString("Consolidation")
    ?? "Host=consolidation-postgres;Port=5432;Database=consolidation;Username=consolidation;Password=consolidation";

builder.Services.AddDbContext<ConsolidationDbContext>(options => options.UseNpgsql(consolidationConnectionString));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<EntryCreatedProjectionProcessor>();
builder.Services.AddSingleton<RabbitMqEntryCreatedConsumer>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Run();

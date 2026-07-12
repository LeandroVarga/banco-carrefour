using BancoCarrefour.Ledger.OutboxPublisher;
using BancoCarrefour.Ledger.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.AddOutboxPublisherObservability();

var ledgerConnectionString = builder.Configuration.GetConnectionString("Ledger")
    ?? "Host=ledger-postgres;Port=5432;Database=ledger;Username=ledger;Password=ledger";

builder.Services.AddDbContextFactory<LedgerDbContext>(options => options.UseNpgsql(ledgerConnectionString));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<OutboxPublisherOptions>(builder.Configuration.GetSection(OutboxPublisherOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RabbitMqOutboxPublisher>();
builder.Services.AddSingleton<IOutboxPublisher>(provider => provider.GetRequiredService<RabbitMqOutboxPublisher>());
builder.Services.AddScoped<OutboxPublishingProcessor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Run();

using BancoCarrefour.Ledger.OutboxPublisher;
using BancoCarrefour.Ledger.Application.PublishOutbox;
using BancoCarrefour.Ledger.Infrastructure.Outbox;
using BancoCarrefour.Ledger.Infrastructure.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.AddOutboxPublisherObservability();

var ledgerConnectionString = LedgerConnectionStringResolver.Resolve(
    builder.Configuration, builder.Environment.EnvironmentName);

builder.Services.Configure<OutboxPublisherOptions>(builder.Configuration.GetSection(OutboxPublisherOptions.SectionName));
builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxPublisherOptions>>().Value;

    return new OutboxPublishingOptions
    {
        BatchSize = options.BatchSize,
        ClaimTimeout = options.ClaimTimeout,
        BaseRetryDelay = options.BaseRetryDelay,
        MaxRetryDelay = options.MaxRetryDelay
    };
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PublisherInstance>();
builder.Services.AddLedgerOutboxInfrastructure(builder.Configuration, ledgerConnectionString);
builder.Services.AddScoped<IPublishPendingEventsUseCase, PublishPendingEventsUseCase>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Run();

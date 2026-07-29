using Amazon.Runtime;
using Amazon.SQS;
using BancoCarrefour.Consolidation.Application;
using BancoCarrefour.Consolidation.Application.ApplyFinancialEntry;
using BancoCarrefour.Consolidation.Infrastructure.DailyBalances;
using BancoCarrefour.Consolidation.Worker;
using BancoCarrefour.Consolidation.Infrastructure;
using BancoCarrefour.Consolidation.Infrastructure.Secrets;
using BancoCarrefour.Consolidation.Worker.Sqs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.AddConsolidationWorkerObservability();

var consolidationConnectionString = ConsolidationConnectionStringResolver.Resolve(
    builder.Configuration, builder.Environment.EnvironmentName);

builder.Services.AddDbContext<ConsolidationDbContext>(options => options.UseNpgsql(consolidationConnectionString));
builder.Services.Configure<SqsOptions>(builder.Configuration.GetSection(SqsOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IDailyBalanceProjectionStore, EfDailyBalanceProjectionStore>();
builder.Services.AddScoped<IApplyFinancialEntryUseCase, ApplyFinancialEntryUseCase>();
builder.Services.AddSingleton<IAmazonSQS>(provider =>
{
    var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SqsOptions>>().Value;
    var config = new AmazonSQSConfig
    {
        ServiceURL = options.ServiceUrl,
        AuthenticationRegion = options.Region
    };

    return new AmazonSQSClient(
        new BasicAWSCredentials(options.AccessKey, options.SecretKey),
        config);
});
builder.Services.AddSingleton<SqsFinancialEntryConsumer>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Run();

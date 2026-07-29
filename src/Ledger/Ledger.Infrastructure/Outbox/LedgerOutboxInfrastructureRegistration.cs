using Amazon.Runtime;
using Amazon.SQS;
using BancoCarrefour.Ledger.Application.PublishOutbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BancoCarrefour.Ledger.Infrastructure.Outbox;

public static class LedgerOutboxInfrastructureRegistration
{
    public static IServiceCollection AddLedgerOutboxInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string ledgerConnectionString)
    {
        services.AddDbContext<LedgerDbContext>(options => options.UseNpgsql(ledgerConnectionString));
        var section = configuration.GetSection(SqsOptions.SectionName);
        var defaults = new SqsOptions();
        var sqsOptions = new SqsOptions
        {
            QueueUrl = section[nameof(SqsOptions.QueueUrl)] ?? defaults.QueueUrl,
            ServiceUrl = section[nameof(SqsOptions.ServiceUrl)] ?? defaults.ServiceUrl,
            Region = section[nameof(SqsOptions.Region)] ?? defaults.Region,
            AccessKey = section[nameof(SqsOptions.AccessKey)] ?? defaults.AccessKey,
            SecretKey = section[nameof(SqsOptions.SecretKey)] ?? defaults.SecretKey
        };
        services.AddSingleton(Options.Create(sqsOptions));
        services.AddSingleton<IAmazonSQS>(provider =>
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
        services.AddScoped<IOutboxStore, PostgresOutboxStore>();
        services.AddSingleton<IIntegrationEventPublisher, SqsIntegrationEventPublisher>();

        return services;
    }
}

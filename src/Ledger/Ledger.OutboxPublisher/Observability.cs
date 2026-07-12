using Microsoft.Extensions.Hosting;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BancoCarrefour.Ledger.OutboxPublisher;

internal static class Observability
{
    public const string ServiceName = "BancoCarrefour.Ledger.OutboxPublisher";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> PublishAttempts = Meter.CreateCounter<long>("ledger.outbox.publish.attempts");
    public static readonly Counter<long> MessagesPublished = Meter.CreateCounter<long>("ledger.outbox.messages.published");
    public static readonly Counter<long> MessagesFailed = Meter.CreateCounter<long>("ledger.outbox.messages.failed");
    public static readonly Counter<long> MessagesUnroutable = Meter.CreateCounter<long>("ledger.outbox.messages.unroutable");
    public static readonly Histogram<double> PublishDuration = Meter.CreateHistogram<double>("ledger.outbox.publish.duration", "ms");

    public static void AddOutboxPublisherObservability(this HostApplicationBuilder builder)
    {
        var serviceVersion = typeof(Observability).Assembly.GetName().Version?.ToString() ?? "unknown";
        var environment = builder.Configuration["DOTNET_ENVIRONMENT"] ?? builder.Environment.EnvironmentName;
        var resource = CreateResource(serviceVersion, environment);
        var hasOtlpEndpoint = HasOtlpEndpoint(builder.Configuration);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;
            logging.SetResourceBuilder(resource);

            if (hasOtlpEndpoint)
            {
                logging.AddOtlpExporter();
            }
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resourceBuilder => resourceBuilder
                .AddService(ServiceName, serviceVersion: serviceVersion)
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment", environment)]))
            .WithTracing(tracing =>
            {
                tracing.AddSource(ServiceName);

                if (hasOtlpEndpoint)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(ServiceName)
                    .AddRuntimeInstrumentation();

                if (hasOtlpEndpoint)
                {
                    metrics.AddOtlpExporter();
                }
            });
    }

    private static ResourceBuilder CreateResource(string serviceVersion, string environment)
    {
        return ResourceBuilder.CreateDefault()
            .AddService(ServiceName, serviceVersion: serviceVersion)
            .AddAttributes([new KeyValuePair<string, object>("deployment.environment", environment)]);
    }

    private static bool HasOtlpEndpoint(IConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
    }
}

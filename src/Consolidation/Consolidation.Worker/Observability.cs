using Microsoft.Extensions.Hosting;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BancoCarrefour.Consolidation.Worker;

internal static class Observability
{
    public const string ServiceName = "BancoCarrefour.Consolidation.Worker";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> EventsConsumed = Meter.CreateCounter<long>("consolidation.events.consumed");
    public static readonly Counter<long> EventsProcessed = Meter.CreateCounter<long>("consolidation.events.processed");
    public static readonly Counter<long> EventsDuplicated = Meter.CreateCounter<long>("consolidation.events.duplicated");
    public static readonly Counter<long> EventsInvalid = Meter.CreateCounter<long>("consolidation.events.invalid");
    public static readonly Counter<long> EventsRetried = Meter.CreateCounter<long>("consolidation.events.retried");
    public static readonly Counter<long> EventsDeadlettered = Meter.CreateCounter<long>("consolidation.events.deadlettered");
    public static readonly Counter<long> EventsProcessingFailed = Meter.CreateCounter<long>("consolidation.events.processing_failed");
    public static readonly Histogram<double> EventProcessDuration = Meter.CreateHistogram<double>("consolidation.event.process.duration", "ms");

    public static void AddConsolidationWorkerObservability(this HostApplicationBuilder builder)
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

using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BancoCarrefour.Ledger.Api;

internal static class Observability
{
    public const string ServiceName = "BancoCarrefour.Ledger.Api";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> EntriesCreated = Meter.CreateCounter<long>("ledger.entries.created");
    public static readonly Counter<long> EntriesReplayed = Meter.CreateCounter<long>("ledger.entries.replayed");
    public static readonly Counter<long> EntriesIdempotencyConflicts = Meter.CreateCounter<long>("ledger.entries.idempotency_conflicts");
    public static readonly Counter<long> EntriesValidationFailed = Meter.CreateCounter<long>("ledger.entries.validation_failed");
    public static readonly Counter<long> EntriesDatabaseUnavailable = Meter.CreateCounter<long>("ledger.entries.database_unavailable");
    public static readonly Histogram<double> EntryCreateDuration = Meter.CreateHistogram<double>("ledger.entry.create.duration", "ms");

    public static void AddLedgerApiObservability(this WebApplicationBuilder builder)
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
                tracing
                    .AddSource(ServiceName)
                    .AddAspNetCoreInstrumentation(options => options.RecordException = true)
                    .AddHttpClientInstrumentation();

                if (hasOtlpEndpoint)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(ServiceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
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

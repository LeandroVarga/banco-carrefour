using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BancoCarrefour.Consolidation.Api;

internal static class Observability
{
    public const string ServiceName = "BancoCarrefour.Consolidation.Api";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> DailyBalanceQueries = Meter.CreateCounter<long>("consolidation.daily_balance.queries");
    public static readonly Counter<long> DailyBalanceFound = Meter.CreateCounter<long>("consolidation.daily_balance.found");
    public static readonly Counter<long> DailyBalanceNotFound = Meter.CreateCounter<long>("consolidation.daily_balance.not_found");
    public static readonly Counter<long> DailyBalanceDatabaseUnavailable = Meter.CreateCounter<long>("consolidation.daily_balance.database_unavailable");
    public static readonly Histogram<double> DailyBalanceQueryDuration = Meter.CreateHistogram<double>("consolidation.daily_balance.query.duration", "ms");

    public static void AddConsolidationApiObservability(this WebApplicationBuilder builder)
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

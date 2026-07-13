using BancoCarrefour.Consolidation.Persistence;
using BancoCarrefour.Consolidation.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var options = LoadTestOptions.FromEnvironment();

Console.WriteLine("Teste de carga do Consolidado");
Console.WriteLine(FormattableString.Invariant($"API: {options.ApiBaseUrl}"));
Console.WriteLine(FormattableString.Invariant($"Merchants: {options.MerchantCount}"));
Console.WriteLine(FormattableString.Invariant($"Datas por merchant: {options.BusinessDateCount}"));
Console.WriteLine(FormattableString.Invariant($"Rampa: {options.RampSeconds}s"));
Console.WriteLine(FormattableString.Invariant($"Carga sustentada: {options.SustainedSeconds}s a {options.TargetRps} RPS"));
Console.WriteLine(FormattableString.Invariant($"Throughput mínimo observado: {options.MinimumObservedRps:F2} RPS"));

await using var dbContext = CreateDbContext(options.ConnectionString);
await PrepareDatasetAsync(dbContext, options);

using var httpClient = new HttpClient
{
    BaseAddress = options.ApiBaseUrl,
    Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds)
};

var targets = CreateTargets(options).ToArray();
var results = await RunLoadAsync(httpClient, targets, options);

var totalSummary = LoadSummary.Create("total", results);
var sustainedSummary = LoadSummary.Create("sustentado", results.Where(x => x.IsSustained));
var plannedTotalRequests = CalculatePlannedRequestCount(options);
var plannedSustainedRequests = options.TargetRps * options.SustainedSeconds;

PrintSummary(totalSummary, plannedTotalRequests);
PrintSummary(sustainedSummary, plannedSustainedRequests, options.MinimumObservedRps);

var executedAsPlanned = sustainedSummary.TotalRequests == plannedSustainedRequests;
var passed = executedAsPlanned
    && sustainedSummary.FailureRate <= options.MaxFailureRate
    && sustainedSummary.ObservedThroughput >= options.MinimumObservedRps
    && sustainedSummary.P95 <= options.MaxP95Milliseconds
    && sustainedSummary.P99 <= options.MaxP99Milliseconds;

Console.WriteLine();
Console.WriteLine("Critérios esperados para a janela sustentada:");
Console.WriteLine(FormattableString.Invariant($"- total executado == total planejado: {executedAsPlanned}"));
Console.WriteLine(FormattableString.Invariant($"- falhas elegíveis <= {options.MaxFailureRate:P2}"));
Console.WriteLine(FormattableString.Invariant($"- throughput observado >= {options.MinimumObservedRps:F2} req/s"));
Console.WriteLine(FormattableString.Invariant($"- p95 <= {options.MaxP95Milliseconds} ms"));
Console.WriteLine(FormattableString.Invariant($"- p99 <= {options.MaxP99Milliseconds} ms"));
Console.WriteLine(passed ? "Resultado: critérios atendidos." : "Resultado: critérios não atendidos.");

return passed ? 0 : 2;

static ConsolidationDbContext CreateDbContext(string connectionString)
{
    var dbContextOptions = new DbContextOptionsBuilder<ConsolidationDbContext>()
        .UseNpgsql(connectionString)
        .Options;

    return new ConsolidationDbContext(dbContextOptions);
}

static async Task PrepareDatasetAsync(ConsolidationDbContext dbContext, LoadTestOptions options)
{
    await dbContext.Database.MigrateAsync();

    var baseDate = DateOnly.ParseExact(options.BaseBusinessDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    var now = DateTimeOffset.UtcNow;

    for (var merchantIndex = 1; merchantIndex <= options.MerchantCount; merchantIndex++)
    {
        var merchantId = FormatMerchantId(merchantIndex);

        for (var dateIndex = 0; dateIndex < options.BusinessDateCount; dateIndex++)
        {
            var businessDate = baseDate.AddDays(dateIndex);
            var credits = 1000m + merchantIndex + dateIndex;
            var debits = 100m + dateIndex;
            var balance = credits - debits;

            var dailyBalance = await dbContext.DailyBalances
                .SingleOrDefaultAsync(x => x.MerchantId == merchantId && x.BusinessDate == businessDate);

            if (dailyBalance is null)
            {
                dbContext.DailyBalances.Add(new DailyBalance
                {
                    DailyBalanceId = Guid.NewGuid(),
                    MerchantId = merchantId,
                    BusinessDate = businessDate,
                    TotalCredits = credits,
                    TotalDebits = debits,
                    Balance = balance,
                    Currency = "BRL",
                    EntryCount = 20 + dateIndex,
                    LastEventOccurredAt = now,
                    LastUpdatedAt = now
                });
            }
            else
            {
                dailyBalance.TotalCredits = credits;
                dailyBalance.TotalDebits = debits;
                dailyBalance.Balance = balance;
                dailyBalance.Currency = "BRL";
                dailyBalance.EntryCount = 20 + dateIndex;
                dailyBalance.LastEventOccurredAt = now;
                dailyBalance.LastUpdatedAt = now;
            }
        }
    }

    await dbContext.SaveChangesAsync();
}

static IEnumerable<RequestTarget> CreateTargets(LoadTestOptions options)
{
    var baseDate = DateOnly.ParseExact(options.BaseBusinessDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    for (var merchantIndex = 1; merchantIndex <= options.MerchantCount; merchantIndex++)
    {
        var merchantId = FormatMerchantId(merchantIndex);
        var token = CreateJwtToken(merchantId, options.SigningKey, options.Issuer, options.Audience);

        for (var dateIndex = 0; dateIndex < options.BusinessDateCount; dateIndex++)
        {
            yield return new RequestTarget(
                merchantId,
                baseDate.AddDays(dateIndex).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                token);
        }
    }
}

static async Task<IReadOnlyCollection<RequestResult>> RunLoadAsync(
    HttpClient httpClient,
    IReadOnlyList<RequestTarget> targets,
    LoadTestOptions options)
{
    var results = new ConcurrentBag<RequestResult>();
    var tasks = new List<Task>();
    var targetIndex = 0;
    var totalSeconds = options.RampSeconds + options.SustainedSeconds;
    var startedAt = Stopwatch.GetTimestamp();

    for (var second = 0; second < totalSeconds; second++)
    {
        var currentRps = second < options.RampSeconds
            ? Math.Max(1, (int)Math.Ceiling(options.TargetRps * (second + 1) / (double)options.RampSeconds))
            : options.TargetRps;

        for (var requestIndex = 0; requestIndex < currentRps; requestIndex++)
        {
            var dueMilliseconds = (second * 1000d) + (requestIndex * 1000d / currentRps);
            var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var delayMilliseconds = dueMilliseconds - elapsedMilliseconds;

            if (delayMilliseconds > 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delayMilliseconds));
            }

            var target = targets[targetIndex++ % targets.Count];
            var isSustained = second >= options.RampSeconds;

            tasks.Add(Task.Run(async () =>
            {
                var result = await SendRequestAsync(httpClient, target, isSustained);
                results.Add(result);
            }));
        }
    }

    await Task.WhenAll(tasks);

    return results;
}

static async Task<RequestResult> SendRequestAsync(HttpClient httpClient, RequestTarget target, bool isSustained)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, $"/daily-balances/{target.BusinessDate}");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
    request.Headers.Add("X-Correlation-Id", $"load-{Guid.NewGuid():N}");

    var startedAt = Stopwatch.GetTimestamp();

    try
    {
        using var response = await httpClient.SendAsync(request);
        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        return new RequestResult(
            IsSuccess: response.IsSuccessStatusCode,
            StatusCode: (int)response.StatusCode,
            DurationMilliseconds: elapsed.TotalMilliseconds,
            IsSustained: isSustained,
            Error: response.IsSuccessStatusCode ? null : response.ReasonPhrase,
            StartedAt: startedAt,
            FinishedAt: Stopwatch.GetTimestamp());
    }
    catch (Exception exception)
    {
        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        return new RequestResult(
            IsSuccess: false,
            StatusCode: 0,
            DurationMilliseconds: elapsed.TotalMilliseconds,
            IsSustained: isSustained,
            Error: exception.GetType().Name,
            StartedAt: startedAt,
            FinishedAt: Stopwatch.GetTimestamp());
    }
}

static string CreateJwtToken(
    string merchantId,
    string signingKey,
    string issuer,
    string audience)
{
    var now = DateTimeOffset.UtcNow;
    var header = new Dictionary<string, object>
    {
        ["alg"] = "HS256",
        ["typ"] = "JWT"
    };
    var payload = new Dictionary<string, object>
    {
        ["sub"] = "load-test-user",
        ["iss"] = issuer,
        ["aud"] = audience,
        ["role"] = "merchant",
        ["merchant_id"] = merchantId,
        ["iat"] = now.ToUnixTimeSeconds(),
        ["exp"] = now.AddHours(1).ToUnixTimeSeconds()
    };

    var unsignedToken = string.Create(
        CultureInfo.InvariantCulture,
        $"{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload))}");

    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
    var signature = hmac.ComputeHash(Encoding.ASCII.GetBytes(unsignedToken));

    return string.Create(
        CultureInfo.InvariantCulture,
        $"{unsignedToken}.{Base64UrlEncode(signature)}");
}

static string Base64UrlEncode(byte[] value)
{
    return Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

static string FormatMerchantId(int merchantIndex)
{
    return string.Create(CultureInfo.InvariantCulture, $"load-merchant-{merchantIndex:000}");
}

static int CalculatePlannedRequestCount(LoadTestOptions options)
{
    var total = 0;
    var totalSeconds = options.RampSeconds + options.SustainedSeconds;

    for (var second = 0; second < totalSeconds; second++)
    {
        total += second < options.RampSeconds
            ? Math.Max(1, (int)Math.Ceiling(options.TargetRps * (second + 1) / (double)options.RampSeconds))
            : options.TargetRps;
    }

    return total;
}

static void PrintSummary(
    LoadSummary summary,
    int plannedRequests,
    double? minimumObservedRps = null)
{
    Console.WriteLine();
    Console.WriteLine(FormattableString.Invariant($"Resumo ({summary.Name})"));
    Console.WriteLine(FormattableString.Invariant($"- total planejado: {plannedRequests}"));
    Console.WriteLine(FormattableString.Invariant($"- total executado: {summary.TotalRequests}"));
    Console.WriteLine(FormattableString.Invariant($"- executado conforme planejado: {summary.TotalRequests == plannedRequests}"));
    Console.WriteLine(FormattableString.Invariant($"- sucessos: {summary.SuccessfulRequests}"));
    Console.WriteLine(FormattableString.Invariant($"- falhas: {summary.FailedRequests}"));
    Console.WriteLine(FormattableString.Invariant($"- taxa de sucesso: {summary.SuccessRate:P2}"));
    Console.WriteLine(FormattableString.Invariant($"- taxa de falha: {summary.FailureRate:P2}"));
    Console.WriteLine(FormattableString.Invariant($"- p95: {summary.P95:F2} ms"));
    Console.WriteLine(FormattableString.Invariant($"- p99: {summary.P99:F2} ms"));
    Console.WriteLine(FormattableString.Invariant($"- throughput observado: {summary.ObservedThroughput:F2} req/s"));

    if (minimumObservedRps.HasValue)
    {
        Console.WriteLine(FormattableString.Invariant($"- throughput mínimo: {minimumObservedRps.Value:F2} req/s"));
    }
}

internal sealed record LoadTestOptions(
    Uri ApiBaseUrl,
    string ConnectionString,
    string SigningKey,
    string Issuer,
    string Audience,
    string BaseBusinessDate,
    int MerchantCount,
    int BusinessDateCount,
    int TargetRps,
    double MinimumObservedRps,
    int RampSeconds,
    int SustainedSeconds,
    int RequestTimeoutSeconds,
    double MaxFailureRate,
    double MaxP95Milliseconds,
    double MaxP99Milliseconds)
{
    public static LoadTestOptions FromEnvironment()
    {
        return new LoadTestOptions(
            ApiBaseUrl: new Uri(Get("CONSOLIDATION_API_BASE_URL", "http://host.docker.internal:8081")),
            ConnectionString: Get(
                "CONSOLIDATION_CONNECTION_STRING",
                "Host=consolidation-postgres;Port=5432;Database=consolidation;Username=consolidation;Password=consolidation"),
            SigningKey: Get("CONSOLIDATION_AUTH_SIGNING_KEY", "ledger-local-development-signing-key-32-bytes"),
            Issuer: Get("CONSOLIDATION_AUTH_ISSUER", "banco-carrefour-local"),
            Audience: Get("CONSOLIDATION_AUTH_AUDIENCE", "banco-carrefour-api"),
            BaseBusinessDate: Get("LOADTEST_BASE_BUSINESS_DATE", "2026-07-01"),
            MerchantCount: GetInt("LOADTEST_MERCHANTS", 20),
            BusinessDateCount: GetInt("LOADTEST_BUSINESS_DATES", 5),
            TargetRps: GetInt("LOADTEST_RPS", 50),
            MinimumObservedRps: GetDouble("LOADTEST_MIN_OBSERVED_RPS", 50),
            RampSeconds: GetInt("LOADTEST_RAMP_SECONDS", 30),
            SustainedSeconds: GetInt("LOADTEST_DURATION_SECONDS", 60),
            RequestTimeoutSeconds: GetInt("LOADTEST_REQUEST_TIMEOUT_SECONDS", 5),
            MaxFailureRate: GetDouble("LOADTEST_MAX_FAILURE_RATE", 0.05),
            MaxP95Milliseconds: GetDouble("LOADTEST_MAX_P95_MS", 500),
            MaxP99Milliseconds: GetDouble("LOADTEST_MAX_P99_MS", 1000));
    }

    private static string Get(string name, string defaultValue)
    {
        return Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : defaultValue;
    }

    private static int GetInt(string name, int defaultValue)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    private static double GetDouble(string name, double defaultValue)
    {
        return double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }
}

internal sealed record RequestTarget(string MerchantId, string BusinessDate, string Token);

internal sealed record RequestResult(
    bool IsSuccess,
    int StatusCode,
    double DurationMilliseconds,
    bool IsSustained,
    string? Error,
    long StartedAt,
    long FinishedAt);

internal sealed record LoadSummary(
    string Name,
    int TotalRequests,
    int SuccessfulRequests,
    int FailedRequests,
    double SuccessRate,
    double FailureRate,
    double P95,
    double P99,
    double ObservedThroughput)
{
    public static LoadSummary Create(string name, IEnumerable<RequestResult> source)
    {
        var results = source.ToArray();

        if (results.Length == 0)
        {
            return new LoadSummary(name, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var successfulRequests = results.Count(x => x.IsSuccess);
        var failedRequests = results.Length - successfulRequests;
        var durations = results.Select(x => x.DurationMilliseconds).Order().ToArray();
        var firstStart = results.Min(x => x.StartedAt);
        var lastFinish = results.Max(x => x.FinishedAt);
        var wallClockSeconds = Math.Max(Stopwatch.GetElapsedTime(firstStart, lastFinish).TotalSeconds, 1d);

        return new LoadSummary(
            name,
            results.Length,
            successfulRequests,
            failedRequests,
            successfulRequests / (double)results.Length,
            failedRequests / (double)results.Length,
            Percentile(durations, 0.95),
            Percentile(durations, 0.99),
            results.Length / wallClockSeconds);
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Count - 1)];
    }
}

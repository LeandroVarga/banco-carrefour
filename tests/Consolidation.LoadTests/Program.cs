using BancoCarrefour.Consolidation.Infrastructure;
using BancoCarrefour.Consolidation.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

var options = LoadTestOptions.FromEnvironment();

if (options.MerchantCredentials.Count == 0)
{
    Console.Error.WriteLine(
        "Nenhuma credencial real de merchant configurada (MERCHANT_A_TEST_CLIENT_SECRET / "
        + "MERCHANT_B_TEST_CLIENT_SECRET). Consolidation.Api só aceita tokens "
        + "reais do Keycloak (OIDC/RS256) - não há bypass HS256 em runtime (ver ADR-0007, "
        + "SecurityRegressionArchitectureTests). Rode scripts/security/bootstrap-local-security.sh "
        + "primeiro e propague os secrets gerados em .local/security/.env.security.");

    return 3;
}

Console.WriteLine("Teste de carga do Consolidado");
Console.WriteLine(FormattableString.Invariant($"API: {options.ApiBaseUrl}"));
Console.WriteLine(FormattableString.Invariant($"Merchants reais disponíveis: {string.Join(", ", options.MerchantCredentials.Select(x => x.MerchantId))}"));
Console.WriteLine(FormattableString.Invariant($"Datas por merchant: {options.BusinessDateCount}"));
Console.WriteLine(FormattableString.Invariant($"Rampa: {options.RampSeconds}s"));
Console.WriteLine(FormattableString.Invariant($"Carga sustentada: {options.SustainedSeconds}s a {options.TargetRps} RPS"));
Console.WriteLine(FormattableString.Invariant($"Throughput mínimo observado: {options.MinimumObservedRps:F2} RPS"));

await using var dbContext = CreateDbContext(options.ConnectionString);
await PrepareDatasetAsync(dbContext, options);

using var tokenHttpClient = new HttpClient();
using var httpClient = new HttpClient
{
    BaseAddress = options.ApiBaseUrl,
    Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds)
};

var targets = (await CreateTargetsAsync(tokenHttpClient, options)).ToArray();
var results = await RunLoadAsync(httpClient, targets, options);

var totalSummary = LoadSummary.Create("total", results);
var sustainedSummary = LoadSummary.Create("sustentado", results.Where(x => x.IsSustained));
var plannedTotalRequests = CalculatePlannedRequestCount(options);
var plannedSustainedRequests = options.TargetRps * options.SustainedSeconds;

PrintSummary(totalSummary, plannedTotalRequests);
PrintSummary(sustainedSummary, plannedSustainedRequests, options.MinimumObservedRps);

var executedAsPlanned = sustainedSummary.TotalRequests == plannedSustainedRequests;

// Critério completo (evidência manual/full-load): inclui p95/p99 - ver
// docs/operations/teste-de-carga-consolidado.md. Não é o mesmo critério do
// gate de CI (ver ciGatePassed abaixo), que não trava em latência (seção 9
// do bloco de CI/desempenho - CI compartilhado não deve reprovar
// a RNF de taxa de falha por ruído de latência do runner).
var passed = executedAsPlanned
    && sustainedSummary.FailureRate <= options.MaxFailureRate
    && sustainedSummary.ObservedThroughput >= options.MinimumObservedRps
    && sustainedSummary.P95 <= options.MaxP95Milliseconds
    && sustainedSummary.P99 <= options.MaxP99Milliseconds;

// Critério do gate de smoke de CI: 50 RPS agendados,
// sem falha de infraestrutura (executado == planejado) e falhas elegíveis
// <= 5%. Falhas de bootstrap/infraestrutura não entram no denominador aqui
// - elas derrubam o job de CI antes mesmo do smoke rodar (ver workflow).
var ciGatePassed = executedAsPlanned && sustainedSummary.FailureRate <= options.MaxFailureRate;

Console.WriteLine();
Console.WriteLine("Critérios esperados para a janela sustentada (evidência completa):");
Console.WriteLine(FormattableString.Invariant($"- total executado == total planejado: {executedAsPlanned}"));
Console.WriteLine(FormattableString.Invariant($"- falhas elegíveis <= {options.MaxFailureRate:P2}"));
Console.WriteLine(FormattableString.Invariant($"- throughput observado >= {options.MinimumObservedRps:F2} req/s"));
Console.WriteLine(FormattableString.Invariant($"- p95 <= {options.MaxP95Milliseconds} ms"));
Console.WriteLine(FormattableString.Invariant($"- p99 <= {options.MaxP99Milliseconds} ms"));
Console.WriteLine(passed ? "Resultado: critérios atendidos." : "Resultado: critérios não atendidos.");
Console.WriteLine(FormattableString.Invariant($"Critério do gate de smoke de CI (RPS agendado + falhas elegíveis <= {options.MaxFailureRate:P2}, sem trava de latência): {(ciGatePassed ? "atendido" : "não atendido")}"));

await WriteEvidenceArtifactsAsync(options, totalSummary, sustainedSummary, plannedTotalRequests, plannedSustainedRequests, ciGatePassed);

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

    for (var merchantIndex = 0; merchantIndex < options.MerchantCredentials.Count; merchantIndex++)
    {
        var merchantId = options.MerchantCredentials[merchantIndex].MerchantId;

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

static async Task<IEnumerable<RequestTarget>> CreateTargetsAsync(HttpClient tokenHttpClient, LoadTestOptions options)
{
    var baseDate = DateOnly.ParseExact(options.BaseBusinessDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    var targets = new List<RequestTarget>();

    foreach (var credential in options.MerchantCredentials)
    {
        var token = await AcquireKeycloakTokenAsync(tokenHttpClient, options.KeycloakTokenUrl, credential.ClientId, credential.ClientSecret, options.KeycloakScope);

        for (var dateIndex = 0; dateIndex < options.BusinessDateCount; dateIndex++)
        {
            targets.Add(new RequestTarget(
                credential.MerchantId,
                baseDate.AddDays(dateIndex).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                token));
        }
    }

    return targets;
}

/// <summary>
/// Obtém um token real via client-credentials do Keycloak real - o mesmo
/// mecanismo aprovado usado pelo runbook e por Security.IntegrationTests
/// (nunca um bypass HS256/local). Chamado direto no Keycloak (rede interna
/// do Compose, sem passar pelo edge-proxy) porque a RNF de 50 RPS mede a
/// capacidade do Consolidation.Api, não o caminho da borda - ver ADR-0012.
/// </summary>
static async Task<string> AcquireKeycloakTokenAsync(
    HttpClient httpClient,
    Uri tokenUrl,
    string clientId,
    string clientSecret,
    string scope)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
    {
        Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = scope
        })
    };

    using var response = await httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
    return payload.GetProperty("access_token").GetString()
        ?? throw new InvalidOperationException("Resposta do Keycloak não contém access_token.");
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

/// <summary>
/// Evidência legível por máquina (JSON) e por humano (Markdown) do smoke de
/// desempenho - usada pelo gate de CI (bloco 1). Escrita só
/// quando as variáveis de ambiente correspondentes são definidas (opt-in,
/// não muda o comportamento padrão do teste de carga para execução manual
/// documentada em docs/operations/teste-de-carga-consolidado.md). Nunca
/// inclui token, senha, connection string ou qualquer valor de secret.
/// </summary>
static async Task WriteEvidenceArtifactsAsync(
    LoadTestOptions options,
    LoadSummary totalSummary,
    LoadSummary sustainedSummary,
    int plannedTotalRequests,
    int plannedSustainedRequests,
    bool ciGatePassed)
{
    if (options.ResultJsonPath is null && options.ResultMarkdownPath is null)
    {
        return;
    }

    var payload = new PerformanceResultPayload(
        Timestamp: DateTimeOffset.UtcNow,
        SourceCommit: options.SourceCommit,
        Target: FormattableString.Invariant($"{options.ApiBaseUrl}daily-balances/{{businessDate}}"),
        ConfiguredRps: options.TargetRps,
        WarmUpSeconds: options.RampSeconds,
        MeasurementSeconds: options.SustainedSeconds,
        TotalScheduled: plannedSustainedRequests,
        TotalCompleted: sustainedSummary.TotalRequests,
        Successes: sustainedSummary.SuccessfulRequests,
        Failures: sustainedSummary.FailedRequests,
        Timeouts: sustainedSummary.TimedOutRequests,
        FailurePercentage: Math.Round(sustainedSummary.FailureRate * 100d, 2),
        P50Milliseconds: Math.Round(sustainedSummary.P50, 2),
        P95Milliseconds: Math.Round(sustainedSummary.P95, 2),
        P99Milliseconds: Math.Round(sustainedSummary.P99, 2),
        ObservedRps: Math.Round(sustainedSummary.ObservedThroughput, 2),
        MaxFailurePercentage: Math.Round(options.MaxFailureRate * 100d, 2),
        Verdict: ciGatePassed ? "pass" : "fail");

    if (options.ResultJsonPath is { } jsonPath)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(jsonPath, json);
        Console.WriteLine(FormattableString.Invariant($"Evidência JSON escrita em: {jsonPath}"));
    }

    if (options.ResultMarkdownPath is { } markdownPath)
    {
        var markdown = FormattableString.Invariant($"""
            ## Smoke de desempenho — {payload.ConfiguredRps} RPS

            | Métrica | Valor |
            |---|---|
            | Commit | `{payload.SourceCommit}` |
            | Alvo | `{payload.Target}` |
            | Aquecimento | {payload.WarmUpSeconds}s |
            | Medição | {payload.MeasurementSeconds}s |
            | Agendadas | {payload.TotalScheduled} |
            | Concluídas | {payload.TotalCompleted} |
            | Sucessos | {payload.Successes} |
            | Falhas | {payload.Failures} |
            | Timeouts (subconjunto de falhas) | {payload.Timeouts} |
            | Taxa de falha | {payload.FailurePercentage}% (máximo {payload.MaxFailurePercentage}%) |
            | p50 | {payload.P50Milliseconds} ms |
            | p95 | {payload.P95Milliseconds} ms |
            | p99 | {payload.P99Milliseconds} ms |
            | RPS observado | {payload.ObservedRps} |
            | Veredito do gate de CI | **{payload.Verdict}** |

            """);
        await File.WriteAllTextAsync(markdownPath, markdown);
        Console.WriteLine(FormattableString.Invariant($"Evidência Markdown escrita em: {markdownPath}"));
    }
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
    Console.WriteLine(FormattableString.Invariant($"- das quais timeout de cliente: {summary.TimedOutRequests}"));
    Console.WriteLine(FormattableString.Invariant($"- taxa de sucesso: {summary.SuccessRate:P2}"));
    Console.WriteLine(FormattableString.Invariant($"- taxa de falha: {summary.FailureRate:P2}"));
    Console.WriteLine(FormattableString.Invariant($"- p50: {summary.P50:F2} ms"));
    Console.WriteLine(FormattableString.Invariant($"- p95: {summary.P95:F2} ms"));
    Console.WriteLine(FormattableString.Invariant($"- p99: {summary.P99:F2} ms"));
    Console.WriteLine(FormattableString.Invariant($"- throughput observado: {summary.ObservedThroughput:F2} req/s"));

    if (minimumObservedRps.HasValue)
    {
        Console.WriteLine(FormattableString.Invariant($"- throughput mínimo: {minimumObservedRps.Value:F2} req/s"));
    }
}

/// <summary>Uma credencial real de client-credentials do Keycloak para um merchant real do realm (ver infra/keycloak/realm/banco-carrefour-realm.json) - nunca um token HS256 auto-assinado.</summary>
internal sealed record MerchantCredential(string MerchantId, string ClientId, string ClientSecret);

internal sealed record LoadTestOptions(
    Uri ApiBaseUrl,
    string ConnectionString,
    Uri KeycloakTokenUrl,
    string KeycloakScope,
    IReadOnlyList<MerchantCredential> MerchantCredentials,
    string BaseBusinessDate,
    int BusinessDateCount,
    int TargetRps,
    double MinimumObservedRps,
    int RampSeconds,
    int SustainedSeconds,
    int RequestTimeoutSeconds,
    double MaxFailureRate,
    double MaxP95Milliseconds,
    double MaxP99Milliseconds,
    string? ResultJsonPath,
    string? ResultMarkdownPath,
    string SourceCommit)
{
    public static LoadTestOptions FromEnvironment()
    {
        return new LoadTestOptions(
            // Alvo direto do Consolidation.Api na rede interna do Compose (não
            // o edge-proxy): a RNF de 50 RPS mede a capacidade do serviço, e o
            // rate limit do WAF na borda (20 r/s, ver infra/edge-proxy) é uma
            // preocupação de segurança separada, já coberta por
            // tests/Security.IntegrationTests/Edge/WafTests.cs - ver ADR-0012.
            // "host.docker.internal:8081" não é usado - as APIs não
            // publicam porta própria (ADR-0008).
            ApiBaseUrl: new Uri(Get("CONSOLIDATION_API_BASE_URL", "http://consolidation-api:8080")),
            ConnectionString: Get(
                "CONSOLIDATION_CONNECTION_STRING",
                "Host=consolidation-postgres;Port=5432;Database=consolidation;Username=consolidation;Password=consolidation"),
            // Direto no Keycloak (rede interna do Compose), pelo mesmo motivo
            // do endpoint de negócio acima - ver ADR-0012.
            KeycloakTokenUrl: new Uri(Get(
                "LOADTEST_KEYCLOAK_TOKEN_URL",
                "http://keycloak:8080/realms/banco-carrefour/protocol/openid-connect/token")),
            KeycloakScope: Get("LOADTEST_KEYCLOAK_SCOPE", "consolidation.read"),
            MerchantCredentials: BuildMerchantCredentials(),
            BaseBusinessDate: Get("LOADTEST_BASE_BUSINESS_DATE", "2026-07-01"),
            BusinessDateCount: GetInt("LOADTEST_BUSINESS_DATES", 5),
            TargetRps: GetInt("LOADTEST_RPS", 50),
            MinimumObservedRps: GetDouble("LOADTEST_MIN_OBSERVED_RPS", 50),
            RampSeconds: GetInt("LOADTEST_RAMP_SECONDS", 30),
            SustainedSeconds: GetInt("LOADTEST_DURATION_SECONDS", 60),
            RequestTimeoutSeconds: GetInt("LOADTEST_REQUEST_TIMEOUT_SECONDS", 5),
            MaxFailureRate: GetDouble("LOADTEST_MAX_FAILURE_RATE", 0.05),
            MaxP95Milliseconds: GetDouble("LOADTEST_MAX_P95_MS", 500),
            MaxP99Milliseconds: GetDouble("LOADTEST_MAX_P99_MS", 1000),
            ResultJsonPath: GetOptional("LOADTEST_RESULT_JSON_PATH"),
            ResultMarkdownPath: GetOptional("LOADTEST_RESULT_MARKDOWN_PATH"),
            SourceCommit: Get("GITHUB_SHA", "local"));
    }

    /// <summary>
    /// Só inclui um merchant se o secret real do respectivo client
    /// (gerado por scripts/security/keycloak-bootstrap.sh, nunca hardcoded
    /// aqui) estiver presente no ambiente - permite rodar com 1 ou 2
    /// merchants reais conforme o que estiver disponível, sem inventar
    /// merchants que não existem no realm.
    /// </summary>
    private static IReadOnlyList<MerchantCredential> BuildMerchantCredentials()
    {
        var credentials = new List<MerchantCredential>();

        if (GetOptional("MERCHANT_A_TEST_CLIENT_SECRET") is { } merchantASecret)
        {
            credentials.Add(new MerchantCredential("merchant-a", "merchant-a-test-client", merchantASecret));
        }

        if (GetOptional("MERCHANT_B_TEST_CLIENT_SECRET") is { } merchantBSecret)
        {
            credentials.Add(new MerchantCredential("merchant-b", "merchant-b-test-client", merchantBSecret));
        }

        return credentials;
    }

    private static string Get(string name, string defaultValue)
    {
        return Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : defaultValue;
    }

    private static string? GetOptional(string name)
    {
        return Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
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

/// <summary>Evidência de desempenho legível por máquina - nunca contém token, senha, connection string ou outro valor de secret (ver seção 14 do bloco de CI/desempenho ).</summary>
internal sealed record PerformanceResultPayload(
    DateTimeOffset Timestamp,
    string SourceCommit,
    string Target,
    int ConfiguredRps,
    int WarmUpSeconds,
    int MeasurementSeconds,
    int TotalScheduled,
    int TotalCompleted,
    int Successes,
    int Failures,
    int Timeouts,
    double FailurePercentage,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double ObservedRps,
    double MaxFailurePercentage,
    string Verdict);

internal sealed record RequestResult(
    bool IsSuccess,
    int StatusCode,
    double DurationMilliseconds,
    bool IsSustained,
    string? Error,
    long StartedAt,
    long FinishedAt)
{
    /// <summary>Timeout do lado do cliente (estourou <see cref="LoadTestOptions.RequestTimeoutSeconds"/>) - contado separadamente de outras falhas para o diagnóstico do resultado, mas ainda é uma requisição elegível (foi de fato disparada contra o serviço real).</summary>
    public bool IsTimeout => Error == nameof(TaskCanceledException) || Error == nameof(OperationCanceledException);
}

internal sealed record LoadSummary(
    string Name,
    int TotalRequests,
    int SuccessfulRequests,
    int FailedRequests,
    int TimedOutRequests,
    double SuccessRate,
    double FailureRate,
    double P50,
    double P95,
    double P99,
    double ObservedThroughput)
{
    public static LoadSummary Create(string name, IEnumerable<RequestResult> source)
    {
        var results = source.ToArray();

        if (results.Length == 0)
        {
            return new LoadSummary(name, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var successfulRequests = results.Count(x => x.IsSuccess);
        var failedRequests = results.Length - successfulRequests;
        var timedOutRequests = results.Count(x => x.IsTimeout);
        var durations = results.Select(x => x.DurationMilliseconds).Order().ToArray();
        var firstStart = results.Min(x => x.StartedAt);
        var lastFinish = results.Max(x => x.FinishedAt);
        var wallClockSeconds = Math.Max(Stopwatch.GetElapsedTime(firstStart, lastFinish).TotalSeconds, 1d);

        return new LoadSummary(
            name,
            results.Length,
            successfulRequests,
            failedRequests,
            timedOutRequests,
            successfulRequests / (double)results.Length,
            failedRequests / (double)results.Length,
            Percentile(durations, 0.50),
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

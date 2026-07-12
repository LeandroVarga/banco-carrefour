using BancoCarrefour.Consolidation.Api.Authentication;
using BancoCarrefour.Consolidation.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;

namespace BancoCarrefour.Consolidation.Api.DailyBalances;

public static class DailyBalanceEndpoints
{
    public static IEndpointRouteBuilder MapDailyBalanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/daily-balances/{businessDate}", GetDailyBalanceAsync)
            .RequireAuthorization(ConsolidationAuthentication.MerchantPolicy);

        return endpoints;
    }

    private static async Task<Results<Ok<DailyBalanceResponse>, BadRequest<ErrorResponse>, NotFound<ErrorResponse>>> GetDailyBalanceAsync(
        string businessDate,
        HttpContext httpContext,
        ConsolidationDbContext dbContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = Observability.ActivitySource.StartActivity("consolidation.daily_balance.query");
        var logger = loggerFactory.CreateLogger("BancoCarrefour.Consolidation.Api.DailyBalances");
        var correlationId = ApiErrorResponses.ResolveCorrelationId(httpContext);

        Observability.DailyBalanceQueries.Add(1);
        activity?.SetTag("correlation.id", correlationId);
        activity?.SetTag("business.date", businessDate);

        if (ApiErrorResponses.HasInvalidCorrelationId(httpContext))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "invalid correlation id");
            Observability.DailyBalanceQueryDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            logger.LogWarning("Consulta do Consolidado rejeitada por correlationId inválido. CorrelationId={CorrelationId}", correlationId);

            return TypedResults.BadRequest(ApiErrorResponses.Create(
                "VALIDATION_ERROR",
                "Requisição inválida.",
                httpContext,
                ["X-Correlation-Id deve ter no máximo 128 caracteres."]));
        }

        if (!DateOnly.TryParseExact(businessDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedBusinessDate))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "invalid businessDate");
            Observability.DailyBalanceQueryDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            logger.LogWarning(
                "Consulta do Consolidado rejeitada por businessDate inválido. BusinessDate={BusinessDate}; CorrelationId={CorrelationId}",
                businessDate,
                correlationId);

            return TypedResults.BadRequest(ApiErrorResponses.Create(
                "VALIDATION_ERROR",
                "Requisição inválida.",
                httpContext,
                ["businessDate deve estar no formato yyyy-MM-dd."]));
        }

        var merchantId = httpContext.User.FindFirstValue(ConsolidationAuthentication.MerchantClaim);
        if (string.IsNullOrWhiteSpace(merchantId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "missing merchant id");
            Observability.DailyBalanceQueryDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            logger.LogWarning("Consulta do Consolidado rejeitada sem merchant_id autenticado. CorrelationId={CorrelationId}", correlationId);

            return TypedResults.BadRequest(ApiErrorResponses.Create(
                "AUTHORIZATION_ERROR",
                "Comerciante autenticado não encontrado.",
                httpContext));
        }

        activity?.SetTag("merchant.id", merchantId);
        logger.LogInformation(
            "Consulta do Consolidado recebida. MerchantId={MerchantId}; BusinessDate={BusinessDate}; CorrelationId={CorrelationId}",
            merchantId,
            parsedBusinessDate,
            correlationId);

        var dailyBalance = await dbContext.DailyBalances
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.MerchantId == merchantId && x.BusinessDate == parsedBusinessDate,
                cancellationToken);

        if (dailyBalance is null)
        {
            Observability.DailyBalanceNotFound.Add(1);
            activity?.SetStatus(ActivityStatusCode.Ok);
            Observability.DailyBalanceQueryDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            logger.LogInformation(
                "Projeção DailyBalance indisponível. MerchantId={MerchantId}; BusinessDate={BusinessDate}; CorrelationId={CorrelationId}",
                merchantId,
                parsedBusinessDate,
                correlationId);

            return TypedResults.NotFound(ApiErrorResponses.Create(
                "DAILY_BALANCE_NOT_FOUND",
                "Projeção DailyBalance não disponível para o comerciante e data informados; isso não confirma saldo zero.",
                httpContext));
        }

        Observability.DailyBalanceFound.Add(1);
        activity?.SetStatus(ActivityStatusCode.Ok);
        Observability.DailyBalanceQueryDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        logger.LogInformation(
            "Projeção DailyBalance encontrada. MerchantId={MerchantId}; BusinessDate={BusinessDate}; CorrelationId={CorrelationId}",
            merchantId,
            parsedBusinessDate,
            correlationId);

        return TypedResults.Ok(new DailyBalanceResponse(
            dailyBalance.MerchantId,
            dailyBalance.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            FormatMoney(dailyBalance.TotalCredits),
            FormatMoney(dailyBalance.TotalDebits),
            FormatMoney(dailyBalance.Balance),
            dailyBalance.Currency,
            dailyBalance.EntryCount,
            dailyBalance.LastUpdatedAt.ToUniversalTime()));
    }

    private static string FormatMoney(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

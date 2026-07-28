using BancoCarrefour.Ledger.Api;
using BancoCarrefour.Ledger.Api.Authentication;
using BancoCarrefour.Ledger.Application.RegisterFinancialEntry;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using System.Diagnostics;
using System.Security.Claims;

namespace BancoCarrefour.Ledger.Api.Entries;

public static class EntryEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapEntryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/entries", CreateEntryAsync)
            .RequireAuthorization(LedgerAuthentication.MerchantPolicy, LedgerAuthentication.LedgerWriteScopePolicy)
            .RequireRateLimiting(BusinessRateLimiting.PolicyName);

        return endpoints;
    }

    private static async Task<Results<Created<CreateEntryResponse>, Ok<CreateEntryResponse>, BadRequest<ErrorResponse>, Conflict<ErrorResponse>, UnprocessableEntity<ErrorResponse>>> CreateEntryAsync(
        CreateEntryRequest request,
        HttpContext httpContext,
        IRegisterFinancialEntryUseCase useCase,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = Observability.ActivitySource.StartActivity("ledger.entry.create");
        var logger = loggerFactory.CreateLogger("BancoCarrefour.Ledger.Api.Entries");
        var correlationId = ApiErrorResponses.ResolveCorrelationId(httpContext);
        activity?.SetTag("correlation.id", correlationId);

        if (ApiErrorResponses.HasInvalidCorrelationId(httpContext))
        {
            Observability.EntriesValidationFailed.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, "invalid correlation id");
            Observability.EntryCreateDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            logger.LogWarning("Registro de lançamento rejeitado por correlationId inválido. CorrelationId={CorrelationId}", correlationId);

            return TypedResults.BadRequest(CreateError(
                "VALIDATION_ERROR",
                "Requisição inválida.",
                correlationId,
                ["X-Correlation-Id deve ter no máximo 128 caracteres."]));
        }

        var merchantId = httpContext.User.FindFirstValue(LedgerAuthentication.MerchantClaim);
        if (string.IsNullOrWhiteSpace(merchantId))
        {
            Observability.EntriesValidationFailed.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, "missing merchant id");
            Observability.EntryCreateDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            logger.LogWarning("Registro de lançamento rejeitado sem merchant_id autenticado. CorrelationId={CorrelationId}", correlationId);

            return TypedResults.BadRequest(CreateError("AUTHORIZATION_ERROR", "Comerciante autenticado não encontrado.", correlationId));
        }

        activity?.SetTag("merchant.id", merchantId);
        var idempotencyKey = httpContext.Request.Headers[IdempotencyKeyHeader].FirstOrDefault();

        RegisterFinancialEntryResult result;
        try
        {
            result = await useCase.RegisterAsync(
                new RegisterFinancialEntryCommand(
                    merchantId,
                    idempotencyKey ?? string.Empty,
                    request.Type ?? string.Empty,
                    request.Amount ?? string.Empty,
                    request.Currency ?? string.Empty,
                    request.OccurredAt ?? default,
                    request.Description,
                    correlationId),
                cancellationToken);
        }
        catch (RegisterFinancialEntryValidationException exception)
        {
            Observability.EntriesValidationFailed.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, "validation failed");
            Observability.EntryCreateDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            logger.LogWarning(
                "Registro de lançamento rejeitado por validação. MerchantId={MerchantId}; IdempotencyKey={IdempotencyKey}; CorrelationId={CorrelationId}; Errors={ValidationErrors}",
                merchantId,
                idempotencyKey,
                correlationId,
                exception.Errors.Count);

            if (exception.Errors.Any(error => error.StartsWith("Idempotency-Key", StringComparison.Ordinal)))
            {
                return TypedResults.BadRequest(CreateError("VALIDATION_ERROR", "Requisição inválida.", correlationId, exception.Errors));
            }

            return TypedResults.UnprocessableEntity(CreateError("VALIDATION_ERROR", "Payload semanticamente inválido.", correlationId, exception.Errors));
        }

        if (result.Status == RegisterFinancialEntryResultStatus.Conflict)
        {
            Observability.EntriesIdempotencyConflicts.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, "idempotency conflict");
            Observability.EntryCreateDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            logger.LogWarning(
                "Conflito de idempotência no registro de lançamento. MerchantId={MerchantId}; IdempotencyKey={IdempotencyKey}; CorrelationId={CorrelationId}",
                merchantId,
                idempotencyKey,
                correlationId);

            return TypedResults.Conflict(CreateError("IDEMPOTENCY_CONFLICT", "Chave de idempotência reutilizada com payload divergente.", correlationId));
        }

        var response = ToResponse(result.Entry!);
        activity?.SetTag("entry.id", response.EntryId);
        activity?.SetTag("business.date", response.BusinessDate);
        activity?.SetStatus(ActivityStatusCode.Ok);
        Observability.EntryCreateDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

        if (result.Status == RegisterFinancialEntryResultStatus.Replay)
        {
            Observability.EntriesReplayed.Add(1);
            logger.LogInformation(
                "Replay idempotente de lançamento. MerchantId={MerchantId}; EntryId={EntryId}; IdempotencyKey={IdempotencyKey}; CorrelationId={CorrelationId}",
                merchantId,
                response.EntryId,
                idempotencyKey,
                correlationId);

            return TypedResults.Ok(response);
        }

        Observability.EntriesCreated.Add(1);
        logger.LogInformation(
            "Lançamento criado. MerchantId={MerchantId}; EntryId={EntryId}; BusinessDate={BusinessDate}; CorrelationId={CorrelationId}",
            merchantId,
            response.EntryId,
            response.BusinessDate,
            correlationId);

        return TypedResults.Created($"/entries/{response.EntryId}", response);
    }

    private static CreateEntryResponse ToResponse(RegisteredFinancialEntry entry)
    {
        return new CreateEntryResponse(
            entry.EntryId,
            entry.MerchantId,
            entry.BusinessDate,
            entry.Type,
            entry.Amount,
            entry.Currency,
            entry.OccurredAt,
            entry.RegisteredAt,
            entry.IdempotencyKey);
    }

    private static ErrorResponse CreateError(
        string errorCode,
        string message,
        string correlationId,
        IReadOnlyCollection<string>? details = null)
    {
        return new ErrorResponse(errorCode, message, correlationId, details);
    }
}

using BancoCarrefour.Ledger.Api;
using BancoCarrefour.Ledger.Api.Authentication;
using BancoCarrefour.Ledger.Persistence;
using BancoCarrefour.Ledger.Persistence.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BancoCarrefour.Ledger.Api.Entries;

public static partial class EntryEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string AmountPattern = "^(?!0+(\\.0{1,2})?$)[0-9]{1,16}(\\.[0-9]{1,2})?$";
    private const string InputIdempotencyUniqueConstraintName = "IX_input_idempotency_merchant_id_idempotency_key";
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapEntryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/entries", CreateEntryAsync)
            .RequireAuthorization(LedgerAuthentication.MerchantPolicy)
            .RequireRateLimiting(BusinessRateLimiting.PolicyName);

        return endpoints;
    }

    private static async Task<Results<Created<CreateEntryResponse>, Ok<CreateEntryResponse>, BadRequest<ErrorResponse>, Conflict<ErrorResponse>, UnprocessableEntity<ErrorResponse>>> CreateEntryAsync(
        CreateEntryRequest request,
        HttpContext httpContext,
        LedgerDbContext dbContext,
        TimeProvider timeProvider,
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
        var validation = Validate(request, idempotencyKey, correlationId);

        if (validation.Errors.Count > 0)
        {
            Observability.EntriesValidationFailed.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, "validation failed");
            Observability.EntryCreateDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            logger.LogWarning(
                "Registro de lançamento rejeitado por validação. MerchantId={MerchantId}; IdempotencyKey={IdempotencyKey}; CorrelationId={CorrelationId}; Errors={ValidationErrors}",
                merchantId,
                idempotencyKey,
                correlationId,
                validation.Errors.Count);

            if (validation.IsBadRequest)
            {
                return TypedResults.BadRequest(CreateError("VALIDATION_ERROR", "Requisição inválida.", correlationId, validation.Errors));
            }

            return TypedResults.UnprocessableEntity(CreateError("VALIDATION_ERROR", "Payload semanticamente inválido.", correlationId, validation.Errors));
        }

        var normalizedType = validation.Type;
        var amount = validation.Amount;
        var normalizedAmount = EntryFingerprint.FormatAmount(amount);
        var normalizedCurrency = validation.Currency;
        var occurredAt = validation.OccurredAt.ToUniversalTime();
        var normalizedDescription = EntryFingerprint.NormalizeDescription(request.Description);
        var businessDate = EntryBusinessDate.FromOccurredAt(occurredAt);
        var fingerprint = EntryFingerprint.Calculate(
            merchantId,
            normalizedType,
            amount,
            normalizedCurrency,
            occurredAt,
            normalizedDescription);

        var existing = await FindExistingResponseAsync(dbContext, merchantId, idempotencyKey!, fingerprint, cancellationToken);

        if (existing.IsConflict)
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

        if (existing.Response is not null)
        {
            Observability.EntriesReplayed.Add(1);
            activity?.SetTag("entry.id", existing.Response.EntryId);
            activity?.SetTag("business.date", existing.Response.BusinessDate);
            activity?.SetStatus(ActivityStatusCode.Ok);
            Observability.EntryCreateDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            logger.LogInformation(
                "Replay idempotente de lançamento. MerchantId={MerchantId}; EntryId={EntryId}; IdempotencyKey={IdempotencyKey}; CorrelationId={CorrelationId}",
                merchantId,
                existing.Response.EntryId,
                idempotencyKey,
                correlationId);

            return TypedResults.Ok(existing.Response);
        }

        var createdAt = timeProvider.GetUtcNow();
        var entryId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var entryType = normalizedType == "CREDIT" ? EntryType.Credit : EntryType.Debit;
        var response = new CreateEntryResponse(
            entryId,
            merchantId,
            businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            normalizedType,
            normalizedAmount,
            normalizedCurrency,
            occurredAt,
            createdAt,
            idempotencyKey!);

        var payload = new EntryCreatedEventPayload(
            eventId,
            "EntryCreated",
            1,
            occurredAt,
            createdAt,
            correlationId,
            entryId,
            merchantId,
            response.BusinessDate,
            normalizedType,
            normalizedAmount,
            normalizedCurrency,
            normalizedDescription);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Entries.Add(new Entry
        {
            EntryId = entryId,
            MerchantId = merchantId,
            BusinessDate = businessDate,
            Type = entryType,
            Amount = amount,
            Currency = normalizedCurrency,
            OccurredAt = occurredAt,
            CreatedAt = createdAt,
            Description = normalizedDescription
        });

        dbContext.InputIdempotencyRecords.Add(new InputIdempotency
        {
            InputIdempotencyId = Guid.NewGuid(),
            MerchantId = merchantId,
            IdempotencyKey = idempotencyKey!,
            PayloadFingerprint = fingerprint,
            EntryId = entryId,
            CreatedAt = createdAt
        });

        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            OutboxId = Guid.NewGuid(),
            EventId = eventId,
            EventType = "EntryCreated",
            EventVersion = 1,
            Payload = JsonSerializer.Serialize(payload, EventJsonOptions),
            Status = OutboxMessageStatus.Pending,
            OccurredAt = occurredAt,
            CreatedAt = createdAt,
            Attempts = 0
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            if (!IsInputIdempotencyUniqueViolation(exception))
            {
                throw;
            }

            var concurrentExisting = await FindExistingResponseAsync(dbContext, merchantId, idempotencyKey!, fingerprint, cancellationToken);

            if (concurrentExisting.Response is not null)
            {
                Observability.EntriesReplayed.Add(1);
                activity?.SetTag("entry.id", concurrentExisting.Response.EntryId);
                activity?.SetTag("business.date", concurrentExisting.Response.BusinessDate);
                activity?.SetStatus(ActivityStatusCode.Ok);
                Observability.EntryCreateDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                logger.LogInformation(
                    "Replay idempotente concorrente de lançamento. MerchantId={MerchantId}; EntryId={EntryId}; IdempotencyKey={IdempotencyKey}; CorrelationId={CorrelationId}",
                    merchantId,
                    concurrentExisting.Response.EntryId,
                    idempotencyKey,
                    correlationId);

                return TypedResults.Ok(concurrentExisting.Response);
            }

            Observability.EntriesIdempotencyConflicts.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, "idempotency conflict");
            Observability.EntryCreateDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            logger.LogWarning(
                "Conflito de idempotência concorrente no registro de lançamento. MerchantId={MerchantId}; IdempotencyKey={IdempotencyKey}; CorrelationId={CorrelationId}",
                merchantId,
                idempotencyKey,
                correlationId);

            return TypedResults.Conflict(CreateError("IDEMPOTENCY_CONFLICT", "Chave de idempotência reutilizada com payload divergente.", correlationId));
        }

        Observability.EntriesCreated.Add(1);
        activity?.SetTag("entry.id", entryId);
        activity?.SetTag("event.id", eventId);
        activity?.SetTag("business.date", response.BusinessDate);
        activity?.SetStatus(ActivityStatusCode.Ok);
        Observability.EntryCreateDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        logger.LogInformation(
            "Lançamento criado. MerchantId={MerchantId}; EntryId={EntryId}; BusinessDate={BusinessDate}; CorrelationId={CorrelationId}",
            merchantId,
            entryId,
            response.BusinessDate,
            correlationId);

        return TypedResults.Created($"/entries/{entryId}", response);
    }

    private static bool IsInputIdempotencyUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName == InputIdempotencyUniqueConstraintName;
    }

    private static async Task<(CreateEntryResponse? Response, bool IsConflict)> FindExistingResponseAsync(
        LedgerDbContext dbContext,
        string merchantId,
        string idempotencyKey,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.InputIdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.MerchantId == merchantId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existing is null)
        {
            return (null, false);
        }

        if (existing.PayloadFingerprint != fingerprint)
        {
            return (null, true);
        }

        var entry = await dbContext.Entries
            .AsNoTracking()
            .SingleAsync(x => x.EntryId == existing.EntryId, cancellationToken);

        var type = entry.Type == EntryType.Credit ? "CREDIT" : "DEBIT";

        return (new CreateEntryResponse(
            entry.EntryId,
            entry.MerchantId,
            entry.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            type,
            EntryFingerprint.FormatAmount(entry.Amount),
            entry.Currency,
            entry.OccurredAt.ToUniversalTime(),
            entry.CreatedAt.ToUniversalTime(),
            existing.IdempotencyKey), false);
    }

    private static ValidationResult Validate(CreateEntryRequest request, string? idempotencyKey, string correlationId)
    {
        var errors = new List<string>();
        var badRequest = false;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            badRequest = true;
            errors.Add("Idempotency-Key é obrigatório.");
        }
        else if (idempotencyKey.Length is < 8 or > 128)
        {
            badRequest = true;
            errors.Add("Idempotency-Key deve ter entre 8 e 128 caracteres.");
        }

        var type = request.Type;
        if (type is not ("CREDIT" or "DEBIT"))
        {
            errors.Add("type deve ser CREDIT ou DEBIT.");
        }

        decimal amount = 0;
        if (request.Amount is null || !AmountRegex().IsMatch(request.Amount))
        {
            errors.Add("amount deve ser monetário positivo conforme contrato.");
        }
        else if (!decimal.TryParse(request.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
        {
            errors.Add("amount deve ser decimal válido.");
        }

        var currency = request.Currency;
        if (currency != "BRL")
        {
            errors.Add("currency deve ser BRL.");
        }

        if (request.OccurredAt is null)
        {
            errors.Add("occurredAt é obrigatório.");
        }

        if (request.Description is { Length: > 256 })
        {
            errors.Add("description deve ter no máximo 256 caracteres.");
        }

        return new ValidationResult(errors, badRequest, type ?? string.Empty, amount, currency ?? string.Empty, request.OccurredAt ?? default);
    }

    private static ErrorResponse CreateError(
        string errorCode,
        string message,
        string correlationId,
        IReadOnlyCollection<string>? details = null)
    {
        return new ErrorResponse(errorCode, message, correlationId, details);
    }

    [GeneratedRegex(AmountPattern, RegexOptions.CultureInvariant)]
    private static partial Regex AmountRegex();

    private sealed record ValidationResult(
        List<string> Errors,
        bool IsBadRequest,
        string Type,
        decimal Amount,
        string Currency,
        DateTimeOffset OccurredAt);
}

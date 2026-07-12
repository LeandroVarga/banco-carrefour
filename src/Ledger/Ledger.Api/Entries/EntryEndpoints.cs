using BancoCarrefour.Ledger.Api.Authentication;
using BancoCarrefour.Ledger.Persistence;
using BancoCarrefour.Ledger.Persistence.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BancoCarrefour.Ledger.Api.Entries;

public static partial class EntryEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private const int CorrelationIdMaxLength = 128;
    private const string AmountPattern = "^(?!0+(\\.0{1,2})?$)[0-9]{1,16}(\\.[0-9]{1,2})?$";
    private const string InputIdempotencyUniqueConstraintName = "IX_input_idempotency_merchant_id_idempotency_key";
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapEntryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/entries", CreateEntryAsync)
            .RequireAuthorization(LedgerAuthentication.MerchantPolicy);

        return endpoints;
    }

    private static async Task<Results<Created<CreateEntryResponse>, Ok<CreateEntryResponse>, BadRequest<ErrorResponse>, Conflict<ErrorResponse>, UnprocessableEntity<ErrorResponse>>> CreateEntryAsync(
        CreateEntryRequest request,
        HttpContext httpContext,
        LedgerDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId(httpContext);

        if (IsCorrelationIdInvalid(httpContext))
        {
            return TypedResults.BadRequest(CreateError(
                "VALIDATION_ERROR",
                "Requisição inválida.",
                correlationId,
                ["X-Correlation-Id deve ter no máximo 128 caracteres."]));
        }

        var merchantId = httpContext.User.FindFirstValue(LedgerAuthentication.MerchantClaim);

        if (string.IsNullOrWhiteSpace(merchantId))
        {
            return TypedResults.BadRequest(CreateError("AUTHORIZATION_ERROR", "Comerciante autenticado não encontrado.", correlationId));
        }

        var idempotencyKey = httpContext.Request.Headers[IdempotencyKeyHeader].FirstOrDefault();
        var validation = Validate(request, idempotencyKey, correlationId);

        if (validation.Errors.Count > 0)
        {
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
            return TypedResults.Conflict(CreateError("IDEMPOTENCY_CONFLICT", "Chave de idempotência reutilizada com payload divergente.", correlationId));
        }

        if (existing.Response is not null)
        {
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
                return TypedResults.Ok(concurrentExisting.Response);
            }

            return TypedResults.Conflict(CreateError("IDEMPOTENCY_CONFLICT", "Chave de idempotência reutilizada com payload divergente.", correlationId));
        }

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

    private static string ResolveCorrelationId(HttpContext httpContext)
    {
        var correlationId = httpContext.Request.Headers[CorrelationIdHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > CorrelationIdMaxLength)
        {
            return TrimToCorrelationIdLimit(httpContext.TraceIdentifier);
        }

        return correlationId;
    }

    private static bool IsCorrelationIdInvalid(HttpContext httpContext)
    {
        var correlationId = httpContext.Request.Headers[CorrelationIdHeader].FirstOrDefault();

        return !string.IsNullOrWhiteSpace(correlationId) && correlationId.Length > CorrelationIdMaxLength;
    }

    private static string TrimToCorrelationIdLimit(string value)
    {
        return value.Length <= CorrelationIdMaxLength ? value : value[..CorrelationIdMaxLength];
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

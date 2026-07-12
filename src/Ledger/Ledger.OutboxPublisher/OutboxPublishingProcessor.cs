using BancoCarrefour.Ledger.Persistence;
using BancoCarrefour.Ledger.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;

namespace BancoCarrefour.Ledger.OutboxPublisher;

public sealed class OutboxPublishingProcessor(
    IDbContextFactory<LedgerDbContext> dbContextFactory,
    IOutboxPublisher publisher,
    TimeProvider timeProvider,
    IOptions<OutboxPublisherOptions> options,
    ILogger<OutboxPublishingProcessor> logger)
{
    private const int LastErrorMaxLength = 2048;

    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var batchSize = Math.Max(1, options.Value.BatchSize);
        var messages = await dbContext.OutboxMessages
            .Where(message => message.Status == OutboxMessageStatus.Pending)
            .OrderBy(message => message.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var published = 0;

        foreach (var message in messages)
        {
            var startedAt = Stopwatch.GetTimestamp();
            var correlationId = TryGetCorrelationId(message.Payload);
            using var activity = Observability.ActivitySource.StartActivity("ledger.outbox.publish");
            using var scope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["correlation_id"] = correlationId,
                ["outbox_id"] = message.OutboxId,
                ["event_id"] = message.EventId
            });

            Observability.PublishAttempts.Add(1);
            activity?.SetTag("correlation.id", correlationId);
            activity?.SetTag("outbox.id", message.OutboxId);
            activity?.SetTag("event.id", message.EventId);
            activity?.SetTag("event.type", message.EventType);

            try
            {
                logger.LogInformation(
                    "Tentativa de publicação da Outbox. OutboxId={OutboxId}; EventId={EventId}; EventType={EventType}; CorrelationId={CorrelationId}; Attempts={Attempts}",
                    message.OutboxId,
                    message.EventId,
                    message.EventType,
                    correlationId,
                    message.Attempts);

                await publisher.PublishAsync(message, cancellationToken);

                message.Status = OutboxMessageStatus.Published;
                message.PublishedAt = timeProvider.GetUtcNow();
                message.LastError = null;

                published++;
                Observability.MessagesPublished.Add(1);
                activity?.SetStatus(ActivityStatusCode.Ok);
                logger.LogInformation(
                    "Mensagem da Outbox publicada. OutboxId={OutboxId}; EventId={EventId}; EventType={EventType}; CorrelationId={CorrelationId}",
                    message.OutboxId,
                    message.EventId,
                    message.EventType,
                    correlationId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.Status = OutboxMessageStatus.Pending;
                message.Attempts++;
                message.LastError = Truncate(exception.Message);

                Observability.MessagesFailed.Add(1);
                activity?.SetStatus(ActivityStatusCode.Error, "outbox publish failed");
                logger.LogWarning(
                    exception,
                    "Falha ao publicar mensagem da Outbox. OutboxId={OutboxId}; EventId={EventId}; EventType={EventType}; CorrelationId={CorrelationId}; Attempts={Attempts}",
                    message.OutboxId,
                    message.EventId,
                    message.EventType,
                    correlationId,
                    message.Attempts);
            }
            finally
            {
                Observability.PublishDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return published;
    }

    private static string Truncate(string value)
    {
        return value.Length <= LastErrorMaxLength ? value : value[..LastErrorMaxLength];
    }

    private static string? TryGetCorrelationId(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);

            return document.RootElement.TryGetProperty("correlationId", out var correlationId)
                ? correlationId.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

using BancoCarrefour.Ledger.Persistence;
using BancoCarrefour.Ledger.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
            try
            {
                await publisher.PublishAsync(message, cancellationToken);

                message.Status = OutboxMessageStatus.Published;
                message.PublishedAt = timeProvider.GetUtcNow();
                message.LastError = null;

                published++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.Status = OutboxMessageStatus.Pending;
                message.Attempts++;
                message.LastError = Truncate(exception.Message);

                logger.LogWarning(
                    exception,
                    "Falha ao publicar mensagem da Outbox {OutboxId}. A mensagem permanecerá pendente.",
                    message.OutboxId);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return published;
    }

    private static string Truncate(string value)
    {
        return value.Length <= LastErrorMaxLength ? value : value[..LastErrorMaxLength];
    }
}

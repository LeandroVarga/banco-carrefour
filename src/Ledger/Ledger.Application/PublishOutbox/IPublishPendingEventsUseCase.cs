namespace BancoCarrefour.Ledger.Application.PublishOutbox;

public interface IPublishPendingEventsUseCase
{
    Task<OutboxPublishResult> PublishAsync(
        CancellationToken cancellationToken);
}

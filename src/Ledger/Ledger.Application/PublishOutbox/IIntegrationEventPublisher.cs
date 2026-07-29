namespace BancoCarrefour.Ledger.Application.PublishOutbox;

public interface IIntegrationEventPublisher
{
    Task PublishAsync(
        OutboxEvent outboxEvent,
        CancellationToken cancellationToken);
}

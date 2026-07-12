using BancoCarrefour.Ledger.Persistence.Entities;

namespace BancoCarrefour.Ledger.OutboxPublisher;

public interface IOutboxPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}

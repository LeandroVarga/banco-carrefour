using BancoCarrefour.Consolidation.Application;

namespace BancoCarrefour.Consolidation.Worker;

public interface IEntryCreatedProjectionProcessor
{
    Task<ProjectionResult> ProcessAsync(
        EntryCreatedEvent message,
        CancellationToken cancellationToken);
}

public sealed class EntryCreatedProjectionProcessorAdapter(
    EntryCreatedProjectionProcessor inner) : IEntryCreatedProjectionProcessor
{
    public Task<ProjectionResult> ProcessAsync(
        EntryCreatedEvent message,
        CancellationToken cancellationToken)
    {
        return inner.ProcessAsync(message, cancellationToken);
    }
}

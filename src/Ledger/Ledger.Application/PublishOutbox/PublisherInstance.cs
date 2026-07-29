namespace BancoCarrefour.Ledger.Application.PublishOutbox;

public sealed class PublisherInstance(string? id = null)
{
    public string Id { get; } = string.IsNullOrWhiteSpace(id)
        ? $"{Environment.MachineName}-{Guid.NewGuid():N}"
        : id;
}

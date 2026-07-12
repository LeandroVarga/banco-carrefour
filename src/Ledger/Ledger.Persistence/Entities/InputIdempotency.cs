namespace BancoCarrefour.Ledger.Persistence.Entities;

public sealed class InputIdempotency
{
    public Guid InputIdempotencyId { get; set; }

    public string MerchantId { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string PayloadFingerprint { get; set; } = string.Empty;

    public Guid EntryId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

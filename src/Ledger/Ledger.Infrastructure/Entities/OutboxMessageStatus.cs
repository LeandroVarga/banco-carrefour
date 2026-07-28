namespace BancoCarrefour.Ledger.Infrastructure.Entities;

public enum OutboxMessageStatus
{
    Pending = 1,
    Processing = 2,
    Published = 3
}

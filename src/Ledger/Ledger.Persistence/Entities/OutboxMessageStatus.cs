namespace BancoCarrefour.Ledger.Persistence.Entities;

public enum OutboxMessageStatus
{
    Pending = 1,
    Published = 2,
    Failed = 3
}

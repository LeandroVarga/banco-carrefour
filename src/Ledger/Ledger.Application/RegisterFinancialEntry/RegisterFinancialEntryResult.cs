namespace BancoCarrefour.Ledger.Application.RegisterFinancialEntry;

public enum RegisterFinancialEntryResultStatus
{
    Created = 1,
    Replay = 2,
    Conflict = 3
}

public sealed record RegisterFinancialEntryResult(
    RegisterFinancialEntryResultStatus Status,
    RegisteredFinancialEntry? Entry)
{
    public static RegisterFinancialEntryResult Created(RegisteredFinancialEntry entry)
    {
        return new RegisterFinancialEntryResult(RegisterFinancialEntryResultStatus.Created, entry);
    }

    public static RegisterFinancialEntryResult Replay(RegisteredFinancialEntry entry)
    {
        return new RegisterFinancialEntryResult(RegisterFinancialEntryResultStatus.Replay, entry);
    }

    public static RegisterFinancialEntryResult Conflict()
    {
        return new RegisterFinancialEntryResult(RegisterFinancialEntryResultStatus.Conflict, null);
    }
}

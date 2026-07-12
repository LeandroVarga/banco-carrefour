namespace BancoCarrefour.Consolidation.Application;

public sealed record ProjectionResult(
    bool Applied,
    bool Duplicate,
    Guid? DailyBalanceId,
    DateOnly? BusinessDate);

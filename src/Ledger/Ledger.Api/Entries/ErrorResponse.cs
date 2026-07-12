namespace BancoCarrefour.Ledger.Api.Entries;

public sealed record ErrorResponse(
    string ErrorCode,
    string Message,
    string CorrelationId,
    IReadOnlyCollection<string>? Details = null);

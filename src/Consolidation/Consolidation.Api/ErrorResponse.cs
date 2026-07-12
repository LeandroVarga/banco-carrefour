namespace BancoCarrefour.Consolidation.Api;

public sealed record ErrorResponse(
    string ErrorCode,
    string Message,
    string CorrelationId,
    IReadOnlyCollection<string>? Details = null);

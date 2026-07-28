using System.Text.Json;
using BancoCarrefour.Consolidation.Application.ApplyFinancialEntry;

namespace BancoCarrefour.Consolidation.Worker.Sqs;

public static class FinancialEntryMessageParser
{
    public static ApplyFinancialEntryCommand Parse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var eventType = root.GetProperty("eventType").GetString() ?? string.Empty;
        var registeredAtProperty = eventType == "EntryCreated" ? "createdAt" : "registeredAt";

        return new ApplyFinancialEntryCommand(
            root.GetProperty("eventId").GetGuid(),
            eventType,
            root.GetProperty("eventVersion").GetInt32(),
            root.GetProperty("occurredAt").GetDateTimeOffset(),
            root.GetProperty(registeredAtProperty).GetDateTimeOffset(),
            root.GetProperty("correlationId").GetString() ?? string.Empty,
            root.GetProperty("entryId").GetGuid(),
            root.GetProperty("merchantId").GetString() ?? string.Empty,
            root.GetProperty("businessDate").GetString() ?? string.Empty,
            root.GetProperty("type").GetString() ?? string.Empty,
            root.GetProperty("amount").GetString() ?? string.Empty,
            root.GetProperty("currency").GetString() ?? string.Empty,
            root.TryGetProperty("description", out var description) && description.ValueKind != JsonValueKind.Null
                ? description.GetString()
                : null);
    }
}

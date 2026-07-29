using System.Text.Json.Serialization;

namespace BancoCarrefour.Contracts.Events;

public sealed record FinancialEntryRegisteredV1(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("entryId")] Guid EntryId,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("eventVersion")] int EventVersion,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("registeredAt")] DateTimeOffset RegisteredAt,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("merchantId")] string MerchantId,
    [property: JsonPropertyName("businessDate")] string BusinessDate,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("amount")] string Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("description")] string? Description);

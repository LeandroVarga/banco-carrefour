using BancoCarrefour.Consolidation.Worker.Sqs;
using Xunit;

namespace BancoCarrefour.Consolidation.IntegrationTests;

public sealed class SqsFinancialEntryMessageParserTests
{
    [Fact]
    public void Parse_aceita_financialEntryRegistered()
    {
        var command = FinancialEntryMessageParser.Parse("""
            {
              "eventId": "11111111-1111-1111-1111-111111111111",
              "entryId": "22222222-2222-2222-2222-222222222222",
              "eventType": "FinancialEntryRegistered",
              "eventVersion": 1,
              "occurredAt": "2026-07-11T13:45:00Z",
              "registeredAt": "2026-07-11T13:45:05Z",
              "correlationId": "corr-test",
              "merchantId": "merchant-001",
              "businessDate": "2026-07-11",
              "type": "CREDIT",
              "amount": "150.75",
              "currency": "BRL",
              "description": "Venda cartão"
            }
            """);

        Assert.Equal("FinancialEntryRegistered", command.EventType);
        Assert.Equal(DateTimeOffset.Parse("2026-07-11T13:45:05Z"), command.RegisteredAt);
        Assert.Equal("merchant-001", command.MerchantId);
    }

    [Fact]
    public void Parse_aceita_entryCreated_legado()
    {
        var command = FinancialEntryMessageParser.Parse("""
            {
              "eventId": "11111111-1111-1111-1111-111111111111",
              "entryId": "22222222-2222-2222-2222-222222222222",
              "eventType": "EntryCreated",
              "eventVersion": 1,
              "occurredAt": "2026-07-11T13:45:00Z",
              "createdAt": "2026-07-11T13:45:05Z",
              "correlationId": "corr-test",
              "merchantId": "merchant-001",
              "businessDate": "2026-07-11",
              "type": "CREDIT",
              "amount": "150.75",
              "currency": "BRL",
              "description": "Venda cartão"
            }
            """);

        Assert.Equal("EntryCreated", command.EventType);
        Assert.Equal(DateTimeOffset.Parse("2026-07-11T13:45:05Z"), command.RegisteredAt);
        Assert.Equal("merchant-001", command.MerchantId);
    }
}

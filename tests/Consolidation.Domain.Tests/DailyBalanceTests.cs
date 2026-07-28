using BancoCarrefour.Consolidation.Domain;
using Xunit;

namespace BancoCarrefour.Consolidation.Domain.Tests;

public sealed class DailyBalanceTests
{
    [Fact]
    public void Apply_soma_creditos_debitos_e_contagem()
    {
        var balance = DailyBalance
            .Start(CreateContribution(FinancialEntryType.Credit, 150.75m, "2026-07-11T13:45:00Z"))
            .Apply(CreateContribution(FinancialEntryType.Debit, 20.25m, "2026-07-11T14:00:00Z"));

        Assert.Equal(150.75m, balance.TotalCredits);
        Assert.Equal(20.25m, balance.TotalDebits);
        Assert.Equal(130.50m, balance.Balance);
        Assert.Equal(2, balance.EntryCount);
        Assert.Equal(DateTimeOffset.Parse("2026-07-11T14:00:00Z"), balance.LastEventOccurredAt);
    }

    [Fact]
    public void Apply_rejeita_contribuicao_de_outro_merchant()
    {
        var balance = DailyBalance.Start(CreateContribution(FinancialEntryType.Credit, 150.75m));
        var otherMerchant = new DailyBalanceContribution(
            MerchantId.Create("merchant-002"),
            BusinessDate.Create(DateOnly.Parse("2026-07-11")),
            FinancialEntryType.Credit,
            Money.CreatePositive(10m),
            DateTimeOffset.Parse("2026-07-11T13:45:00Z"));

        Assert.Throws<DomainValidationException>(() => balance.Apply(otherMerchant));
    }

    [Theory]
    [InlineData("")]
    [InlineData("PIX")]
    public void FinancialEntryTypeParser_rejeita_tipo_invalido(string value)
    {
        Assert.Throws<DomainValidationException>(() => FinancialEntryTypeParser.Parse(value));
    }

    private static DailyBalanceContribution CreateContribution(
        FinancialEntryType type,
        decimal amount,
        string occurredAt = "2026-07-11T13:45:00Z")
    {
        return new DailyBalanceContribution(
            MerchantId.Create("merchant-001"),
            BusinessDate.Create(DateOnly.Parse("2026-07-11")),
            type,
            Money.CreatePositive(amount),
            DateTimeOffset.Parse(occurredAt));
    }
}

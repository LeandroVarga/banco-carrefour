using BancoCarrefour.Ledger.Domain;
using Xunit;

namespace BancoCarrefour.Ledger.Domain.Tests;

public sealed class FinancialEntryTests
{
    [Fact]
    public void Register_credito_cria_lancamento_financeiro_imutavel()
    {
        var entry = CreateEntry(EntryType.Credit, 150.75m);

        Assert.Equal(EntryType.Credit, entry.Type);
        Assert.Equal(150.75m, entry.Money.Amount);
        Assert.Equal("BRL", entry.Money.Currency);
        Assert.Equal("Venda cartão", entry.Description.Value);
        Assert.Equal(DateTimeOffset.Parse("2026-07-11T13:45:00Z"), entry.OccurredAt);
        Assert.Equal(DateTimeOffset.Parse("2026-07-11T13:45:05Z"), entry.RegisteredAt);
    }

    [Fact]
    public void Register_debito_preserva_tipo_de_movimentacao()
    {
        var entry = CreateEntry(EntryType.Debit, 25.10m);

        Assert.Equal(EntryType.Debit, entry.Type);
        Assert.Equal(25.10m, entry.Money.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Money_rejeita_valor_nao_positivo(decimal amount)
    {
        Assert.Throws<DomainValidationException>(() => Money.Create(amount, "BRL"));
    }

    [Fact]
    public void Money_rejeita_precisao_maior_que_duas_casas()
    {
        Assert.Throws<DomainValidationException>(() => Money.Create(10.123m, "BRL"));
    }

    [Fact]
    public void Money_rejeita_moeda_diferente_de_brl()
    {
        Assert.Throws<DomainValidationException>(() => Money.Create(10m, "USD"));
    }

    [Theory]
    [InlineData("CREDIT", EntryType.Credit)]
    [InlineData("DEBIT", EntryType.Debit)]
    public void EntryTypeParser_aceita_tipos_do_contrato(string value, EntryType expected)
    {
        Assert.Equal(expected, EntryTypeParser.Parse(value));
    }

    [Fact]
    public void EntryTypeParser_rejeita_tipo_invalido()
    {
        Assert.Throws<DomainValidationException>(() => EntryTypeParser.Parse("Pending"));
    }

    [Fact]
    public void BusinessDate_e_derivada_de_occurredAt_em_America_Sao_Paulo()
    {
        var entry = CreateEntry(EntryType.Credit, 10m, occurredAt: DateTimeOffset.Parse("2026-07-12T02:30:00Z"));

        Assert.Equal(new DateOnly(2026, 7, 11), entry.BusinessDate.Value);
    }

    [Fact]
    public void Lancamento_retroativo_e_aceito()
    {
        var entry = CreateEntry(EntryType.Credit, 10m, occurredAt: DateTimeOffset.Parse("2020-01-01T10:00:00Z"));

        Assert.Equal(new DateOnly(2020, 1, 1), entry.BusinessDate.Value);
    }

    [Fact]
    public void Description_e_normalizada()
    {
        var description = EntryDescription.Create("  Venda local  ");

        Assert.Equal("Venda local", description.Value);
    }

    [Fact]
    public void Description_vazia_vira_nula()
    {
        var description = EntryDescription.Create("   ");

        Assert.Null(description.Value);
    }

    private static FinancialEntry CreateEntry(
        EntryType type,
        decimal amount,
        DateTimeOffset? occurredAt = null)
    {
        return FinancialEntry.Register(
            EntryId.New(),
            MerchantId.Create("merchant-001"),
            type,
            Money.Create(amount, "BRL"),
            occurredAt ?? DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            DateTimeOffset.Parse("2026-07-11T13:45:05Z"),
            EntryDescription.Create("Venda cartão"));
    }
}

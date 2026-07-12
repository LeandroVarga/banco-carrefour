using BancoCarrefour.Ledger.Persistence;
using BancoCarrefour.Ledger.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BancoCarrefour.Ledger.IntegrationTests;

public sealed class LedgerPersistenceModelTests
{
    [Fact]
    public void LedgerDbContext_deve_expor_dbsets_esperados()
    {
        using var context = CreateContext();

        Assert.NotNull(context.Entries);
        Assert.NotNull(context.InputIdempotencyRecords);
        Assert.NotNull(context.OutboxMessages);
    }

    [Fact]
    public void Entry_amount_deve_ser_numeric_18_2()
    {
        var property = GetProperty<Entry>(nameof(Entry.Amount));

        Assert.Equal("numeric(18,2)", property.GetColumnType());
    }

    [Fact]
    public void Entry_businessDate_deve_ser_date()
    {
        var property = GetProperty<Entry>(nameof(Entry.BusinessDate));

        Assert.Equal("date", property.GetColumnType());
    }

    [Fact]
    public void Entry_type_deve_persistir_valores_alinhados_aos_contratos()
    {
        var property = GetProperty<Entry>(nameof(Entry.Type));
        var converter = property.GetTypeMapping().Converter;

        Assert.NotNull(converter);
        Assert.Equal("CREDIT", converter.ConvertToProvider(EntryType.Credit));
        Assert.Equal("DEBIT", converter.ConvertToProvider(EntryType.Debit));
    }

    [Fact]
    public void Entries_deve_ter_chave_primaria_entryId()
    {
        var key = GetEntityType<Entry>().FindPrimaryKey();

        Assert.NotNull(key);
        Assert.Equal([nameof(Entry.EntryId)], key.Properties.Select(x => x.Name));
    }

    [Fact]
    public void InputIdempotency_deve_ter_indice_unico_por_merchantId_e_idempotencyKey()
    {
        var index = FindIndex<InputIdempotency>(nameof(InputIdempotency.MerchantId), nameof(InputIdempotency.IdempotencyKey));

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Outbox_deve_ter_chave_primaria_outboxId()
    {
        var key = GetEntityType<OutboxMessage>().FindPrimaryKey();

        Assert.NotNull(key);
        Assert.Equal([nameof(OutboxMessage.OutboxId)], key.Properties.Select(x => x.Name));
    }

    [Fact]
    public void Outbox_deve_ter_indice_unico_por_eventId()
    {
        var index = FindIndex<OutboxMessage>(nameof(OutboxMessage.EventId));

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Outbox_payload_deve_ser_jsonb()
    {
        var property = GetProperty<OutboxMessage>(nameof(OutboxMessage.Payload));

        Assert.Equal("jsonb", property.GetColumnType());
    }

    [Fact]
    public void Modelo_nao_deve_usar_float_ou_double_para_dinheiro()
    {
        var moneyProperties = GetEntityType<Entry>()
            .GetProperties()
            .Where(x => x.Name.Contains("Amount", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(moneyProperties);
        Assert.All(moneyProperties, property =>
        {
            Assert.NotEqual(typeof(float), property.ClrType);
            Assert.NotEqual(typeof(double), property.ClrType);
        });
    }

    private static LedgerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=metadata_only;Password=metadata_only")
            .Options;

        return new LedgerDbContext(options);
    }

    private static IReadOnlyEntityType GetEntityType<TEntity>()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(TEntity));

        Assert.NotNull(entityType);

        return entityType;
    }

    private static IReadOnlyProperty GetProperty<TEntity>(string propertyName)
    {
        var property = GetEntityType<TEntity>().FindProperty(propertyName);

        Assert.NotNull(property);

        return property;
    }

    private static IReadOnlyIndex? FindIndex<TEntity>(params string[] propertyNames)
    {
        return GetEntityType<TEntity>()
            .GetIndexes()
            .SingleOrDefault(index => index.Properties.Select(x => x.Name).SequenceEqual(propertyNames));
    }
}

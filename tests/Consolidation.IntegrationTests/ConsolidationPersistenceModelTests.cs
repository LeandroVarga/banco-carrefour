using BancoCarrefour.Consolidation.Persistence;
using BancoCarrefour.Consolidation.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BancoCarrefour.Consolidation.IntegrationTests;

public sealed class ConsolidationPersistenceModelTests : IAsyncLifetime
{
    private readonly ConsolidationTestDatabase database = new();

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        await using var context = CreateContext();

        await context.Database.MigrateAsync();
        await context.ProcessedEvents.ExecuteDeleteAsync();
        await context.DailyBalances.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync()
    {
        await database.DisposeAsync();
    }

    [Fact]
    public void ConsolidationDbContext_deve_expor_dbsets_esperados()
    {
        using var context = CreateMetadataContext();

        Assert.NotNull(context.DailyBalances);
        Assert.NotNull(context.ProcessedEvents);
    }

    [Fact]
    public void DailyBalance_deve_ter_indice_unico_por_merchantId_e_businessDate()
    {
        var index = FindIndex<DailyBalance>(nameof(DailyBalance.MerchantId), nameof(DailyBalance.BusinessDate));

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
    }

    [Theory]
    [InlineData(nameof(DailyBalance.TotalCredits))]
    [InlineData(nameof(DailyBalance.TotalDebits))]
    [InlineData(nameof(DailyBalance.Balance))]
    public void DailyBalance_deve_mapear_campos_monetarios_como_numeric_18_2(string propertyName)
    {
        var property = GetProperty<DailyBalance>(propertyName);

        Assert.Equal("numeric(18,2)", property.GetColumnType());
        Assert.NotEqual(typeof(float), property.ClrType);
        Assert.NotEqual(typeof(double), property.ClrType);
    }

    [Fact]
    public void ProcessedEvent_deve_ter_indice_unico_por_eventId()
    {
        var index = FindIndex<ProcessedEvent>(nameof(ProcessedEvent.EventId));

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
    }

    [Fact]
    public async Task DbContext_deve_aplicar_migration_em_postgresql_real()
    {
        await using var context = CreateContext();

        await context.Database.MigrateAsync();

        Assert.True(await context.Database.CanConnectAsync());
    }

    [Fact]
    public async Task Deve_inserir_dailyBalance_valido()
    {
        await using var context = CreateContext();

        context.DailyBalances.Add(CreateDailyBalance("merchant-001", new DateOnly(2026, 7, 11)));

        await context.SaveChangesAsync();

        var persisted = await context.DailyBalances.AsNoTracking().SingleAsync();
        Assert.Equal("merchant-001", persisted.MerchantId);
        Assert.Equal(new DateOnly(2026, 7, 11), persisted.BusinessDate);
        Assert.Equal(150.75m, persisted.TotalCredits);
        Assert.Equal(25.10m, persisted.TotalDebits);
        Assert.Equal(125.65m, persisted.Balance);
    }

    [Fact]
    public async Task Deve_impedir_dailyBalance_duplicado_por_merchantId_e_businessDate()
    {
        await using var context = CreateContext();
        var businessDate = new DateOnly(2026, 7, 11);

        context.DailyBalances.Add(CreateDailyBalance("merchant-001", businessDate));
        context.DailyBalances.Add(CreateDailyBalance("merchant-001", businessDate));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Deve_impedir_processedEvent_duplicado_por_eventId()
    {
        await using var context = CreateContext();
        var eventId = Guid.NewGuid();

        context.ProcessedEvents.Add(CreateProcessedEvent(eventId));
        context.ProcessedEvents.Add(CreateProcessedEvent(eventId));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private ConsolidationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;

        return new ConsolidationDbContext(options);
    }

    private static ConsolidationDbContext CreateMetadataContext()
    {
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=metadata_only;Password=metadata_only")
            .Options;

        return new ConsolidationDbContext(options);
    }

    private static DailyBalance CreateDailyBalance(string merchantId, DateOnly businessDate)
    {
        return new DailyBalance
        {
            DailyBalanceId = Guid.NewGuid(),
            MerchantId = merchantId,
            BusinessDate = businessDate,
            TotalCredits = 150.75m,
            TotalDebits = 25.10m,
            Balance = 125.65m,
            Currency = "BRL",
            EntryCount = 2,
            LastEventOccurredAt = DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            LastUpdatedAt = DateTimeOffset.Parse("2026-07-11T13:45:05Z")
        };
    }

    private static ProcessedEvent CreateProcessedEvent(Guid eventId)
    {
        return new ProcessedEvent
        {
            ProcessedEventId = Guid.NewGuid(),
            EventId = eventId,
            EventType = "EntryCreated",
            EventVersion = 1,
            MerchantId = "merchant-001",
            BusinessDate = new DateOnly(2026, 7, 11),
            ProcessedAt = DateTimeOffset.Parse("2026-07-11T13:45:10Z")
        };
    }

    private static IReadOnlyEntityType GetEntityType<TEntity>()
    {
        using var context = CreateMetadataContext();
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

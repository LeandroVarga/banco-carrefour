using BancoCarrefour.Consolidation.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BancoCarrefour.Consolidation.Persistence;

public sealed class ConsolidationDbContext(DbContextOptions<ConsolidationDbContext> options) : DbContext(options)
{
    public DbSet<DailyBalance> DailyBalances => Set<DailyBalance>();

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureDailyBalances(modelBuilder);
        ConfigureProcessedEvents(modelBuilder);
    }

    private static void ConfigureDailyBalances(ModelBuilder modelBuilder)
    {
        var dailyBalance = modelBuilder.Entity<DailyBalance>();

        dailyBalance.ToTable("daily_balances");
        dailyBalance.HasKey(x => x.DailyBalanceId);
        dailyBalance.HasIndex(x => new { x.MerchantId, x.BusinessDate }).IsUnique();

        dailyBalance.Property(x => x.DailyBalanceId).HasColumnName("daily_balance_id");
        dailyBalance.Property(x => x.MerchantId).HasColumnName("merchant_id").HasMaxLength(64).IsRequired();
        dailyBalance.Property(x => x.BusinessDate).HasColumnName("business_date").HasColumnType("date");
        dailyBalance.Property(x => x.TotalCredits).HasColumnName("total_credits").HasColumnType("numeric(18,2)");
        dailyBalance.Property(x => x.TotalDebits).HasColumnName("total_debits").HasColumnType("numeric(18,2)");
        dailyBalance.Property(x => x.Balance).HasColumnName("balance").HasColumnType("numeric(18,2)");
        dailyBalance.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        dailyBalance.Property(x => x.EntryCount).HasColumnName("entry_count");
        dailyBalance.Property(x => x.LastEventOccurredAt).HasColumnName("last_event_occurred_at");
        dailyBalance.Property(x => x.LastUpdatedAt).HasColumnName("last_updated_at");
    }

    private static void ConfigureProcessedEvents(ModelBuilder modelBuilder)
    {
        var processedEvent = modelBuilder.Entity<ProcessedEvent>();

        processedEvent.ToTable("processed_events");
        processedEvent.HasKey(x => x.ProcessedEventId);
        processedEvent.HasIndex(x => x.EventId).IsUnique();

        processedEvent.Property(x => x.ProcessedEventId).HasColumnName("processed_event_id");
        processedEvent.Property(x => x.EventId).HasColumnName("event_id");
        processedEvent.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        processedEvent.Property(x => x.EventVersion).HasColumnName("event_version");
        processedEvent.Property(x => x.MerchantId).HasColumnName("merchant_id").HasMaxLength(64).IsRequired();
        processedEvent.Property(x => x.BusinessDate).HasColumnName("business_date").HasColumnType("date");
        processedEvent.Property(x => x.ProcessedAt).HasColumnName("processed_at");
    }
}

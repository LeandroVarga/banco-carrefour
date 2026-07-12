using BancoCarrefour.Ledger.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BancoCarrefour.Ledger.Persistence;

public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    public DbSet<Entry> Entries => Set<Entry>();

    public DbSet<InputIdempotency> InputIdempotencyRecords => Set<InputIdempotency>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureEntries(modelBuilder);
        ConfigureInputIdempotency(modelBuilder);
        ConfigureOutboxMessages(modelBuilder);
    }

    private static void ConfigureEntries(ModelBuilder modelBuilder)
    {
        var entry = modelBuilder.Entity<Entry>();

        entry.ToTable("entries");
        entry.HasKey(x => x.EntryId);

        entry.Property(x => x.EntryId).HasColumnName("entry_id");
        entry.Property(x => x.MerchantId).HasColumnName("merchant_id").HasMaxLength(64).IsRequired();
        entry.Property(x => x.BusinessDate).HasColumnName("business_date").HasColumnType("date");
        entry.Property(x => x.Type)
            .HasColumnName("type")
            .HasConversion(
                value => value == EntryType.Credit ? "CREDIT" : "DEBIT",
                value => value == "CREDIT" ? EntryType.Credit : EntryType.Debit)
            .HasMaxLength(16)
            .IsRequired();
        entry.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");
        entry.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        entry.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        entry.Property(x => x.CreatedAt).HasColumnName("created_at");
        entry.Property(x => x.Description).HasColumnName("description").HasMaxLength(256);
    }

    private static void ConfigureInputIdempotency(ModelBuilder modelBuilder)
    {
        var inputIdempotency = modelBuilder.Entity<InputIdempotency>();

        inputIdempotency.ToTable("input_idempotency");
        inputIdempotency.HasKey(x => x.InputIdempotencyId);
        inputIdempotency.HasIndex(x => new { x.MerchantId, x.IdempotencyKey }).IsUnique();

        inputIdempotency.Property(x => x.InputIdempotencyId).HasColumnName("input_idempotency_id");
        inputIdempotency.Property(x => x.MerchantId).HasColumnName("merchant_id").HasMaxLength(64).IsRequired();
        inputIdempotency.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128).IsRequired();
        inputIdempotency.Property(x => x.PayloadFingerprint).HasColumnName("payload_fingerprint").HasMaxLength(128).IsRequired();
        inputIdempotency.Property(x => x.EntryId).HasColumnName("entry_id");
        inputIdempotency.Property(x => x.CreatedAt).HasColumnName("created_at");
    }

    private static void ConfigureOutboxMessages(ModelBuilder modelBuilder)
    {
        var outboxMessage = modelBuilder.Entity<OutboxMessage>();

        outboxMessage.ToTable("outbox_messages");
        outboxMessage.HasKey(x => x.OutboxId);
        outboxMessage.HasIndex(x => x.EventId).IsUnique();
        outboxMessage.HasIndex(x => new { x.Status, x.CreatedAt });

        outboxMessage.Property(x => x.OutboxId).HasColumnName("outbox_id");
        outboxMessage.Property(x => x.EventId).HasColumnName("event_id");
        outboxMessage.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(128).IsRequired();
        outboxMessage.Property(x => x.EventVersion).HasColumnName("event_version");
        outboxMessage.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        outboxMessage.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        outboxMessage.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        outboxMessage.Property(x => x.CreatedAt).HasColumnName("created_at");
        outboxMessage.Property(x => x.PublishedAt).HasColumnName("published_at");
        outboxMessage.Property(x => x.Attempts).HasColumnName("attempts");
        outboxMessage.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(2048);
        outboxMessage.Property(x => x.LockedAt).HasColumnName("locked_at");
    }
}

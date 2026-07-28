using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BancoCarrefour.Ledger.Infrastructure;

public sealed class LedgerDbContextFactory : IDesignTimeDbContextFactory<LedgerDbContext>
{
    public LedgerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LedgerDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=banco_carrefour_ledger;Username=ledger;Password=ledger");

        return new LedgerDbContext(optionsBuilder.Options);
    }
}

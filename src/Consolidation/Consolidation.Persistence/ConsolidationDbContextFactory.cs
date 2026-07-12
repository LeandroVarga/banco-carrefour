using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BancoCarrefour.Consolidation.Persistence;

public sealed class ConsolidationDbContextFactory : IDesignTimeDbContextFactory<ConsolidationDbContext>
{
    public ConsolidationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ConsolidationDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5433;Database=consolidation;Username=consolidation;Password=consolidation");

        return new ConsolidationDbContext(optionsBuilder.Options);
    }
}

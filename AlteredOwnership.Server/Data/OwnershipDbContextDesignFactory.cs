using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AlteredOwnership.Server.Data;

// Used only by `dotnet ef` at design time to generate migrations.
// At runtime the DbContext is configured by Aspire via AddNpgsqlDbContext.
public class OwnershipDbContextDesignFactory : IDesignTimeDbContextFactory<OwnershipDbContext>
{
    public OwnershipDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OwnershipDbContext>()
            .UseNpgsql("Host=localhost;Database=ownershipdb;Username=postgres;Password=postgres")
            .Options;
        return new OwnershipDbContext(options);
    }
}

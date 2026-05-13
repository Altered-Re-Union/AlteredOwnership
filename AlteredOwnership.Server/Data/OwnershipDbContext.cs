using AlteredOwnership.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Data;

public class OwnershipDbContext(DbContextOptions<OwnershipDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<OwnershipEvent> OwnershipEvents => Set<OwnershipEvent>();
    public DbSet<CardOwnership> CardOwnerships => Set<CardOwnership>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.KeycloakId).IsRequired();
            e.HasIndex(x => x.KeycloakId).IsUnique();
        });

        b.Entity<OwnershipEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.Kind).HasConversion<string>();
            e.HasIndex(x => new { x.UserId, x.UserEventId }).IsUnique();
        });

        b.Entity<CardOwnership>(e =>
        {
            e.HasKey(x => new { x.UserId, x.CardReference });
            e.Property(x => x.CardReference).IsRequired();
            e.ToTable(t => t.HasCheckConstraint(
                "CK_CardOwnerships_UniqueQuantityOne",
                "(\"IsUnique\" = false) OR (\"Quantity\" = 1)"));
            e.HasIndex(x => x.CardReference)
                .IsUnique()
                .HasFilter("\"IsUnique\" = true");
        });
    }
}

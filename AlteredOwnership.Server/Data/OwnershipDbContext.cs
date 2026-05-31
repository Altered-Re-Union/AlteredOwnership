using System.Text.Json;
using AlteredOwnership.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AlteredOwnership.Server.Data;

public class OwnershipDbContext(DbContextOptions<OwnershipDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<OwnershipEvent> OwnershipEvents => Set<OwnershipEvent>();
    public DbSet<CardOwnership> CardOwnerships => Set<CardOwnership>();
    public DbSet<Card> Cards => Set<Card>();

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
            e.HasIndex(x => x.PayloadHash)
                .IsUnique()
                .HasFilter("\"PayloadHash\" IS NOT NULL");
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

        b.Entity<Card>(e =>
        {
            e.HasKey(x => x.Reference);
            e.Property(x => x.Faction).IsRequired();
            e.Property(x => x.Rarity).IsRequired();
            e.Property(x => x.CardType).IsRequired();
            e.Property(x => x.Set).IsRequired();
            e.Property(x => x.Variation).IsRequired();
            e.HasIndex(x => x.Faction);
            e.HasIndex(x => x.Rarity);
            e.HasIndex(x => x.CardType);

            // Per-language text stored as jsonb. Explicit System.Text.Json converter +
            // ValueComparer so change tracking detects dictionary mutations, without
            // depending on the Npgsql dynamic-JSON data-source feature.
            e.Property(x => x.Name).HasColumnType("jsonb").HasConversion(LocalizedTextConverter, LocalizedTextComparer);
            e.Property(x => x.ImagePath).HasColumnType("jsonb").HasConversion(LocalizedTextConverter, LocalizedTextComparer);
        });
    }

    private static readonly ValueConverter<Dictionary<string, string>, string> LocalizedTextConverter = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => string.IsNullOrEmpty(v)
            ? new()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new());

    private static readonly ValueComparer<Dictionary<string, string>> LocalizedTextComparer = new(
        (a, c) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c, (JsonSerializerOptions?)null),
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
        v => JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null) ?? new());
}

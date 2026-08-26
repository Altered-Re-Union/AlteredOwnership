using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AlteredOwnership.Server.Tests.Integration;

// Schema-level regression guards for check constraints that no current app code path
// actually exercises. BoosterInventory's equivalent (CK_BoosterInventories_QuantityNonNegative)
// is already covered end-to-end through the API in
// BoosterTests.Opening_more_boosters_than_owned_fails_and_grants_nothing — CardOwnership has
// no "spend/decrement a card" feature yet (only CollectionImporter writes Quantity, always
// replacing it with a freshly-replayed non-negative total), so there's no app path to drive
// it negative through. These tests instead hit the constraint directly via the DbContext, so
// a regression (constraint dropped or renamed) is still caught even before such a feature
// exists.
public class DataConstraintsTests(OwnershipApiFactory factory) : IClassFixture<OwnershipApiFactory>
{
    private async Task<Guid> SeedUserAsync(string keycloakId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        var user = new User { Id = Guid.NewGuid(), KeycloakId = keycloakId, CreatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task CardOwnership_quantity_cannot_go_negative()
    {
        var userId = await SeedUserAsync("card-quantity-guard-user");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        db.CardOwnerships.Add(new CardOwnership
        {
            UserId = userId,
            CardReference = "ALT_CONSTRAINT_GUARD_C_AX_01_C",
            Quantity = -1,
            IsUnique = false,
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var pgEx = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal("23514", pgEx.SqlState); // check_violation
        Assert.Equal("CK_CardOwnerships_QuantityNonNegative", pgEx.ConstraintName);
    }

    [Fact]
    public async Task BoosterInventory_quantity_cannot_go_negative()
    {
        var userId = await SeedUserAsync("booster-quantity-guard-user");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        db.BoosterInventories.Add(new BoosterInventory
        {
            UserId = userId,
            BoosterTypeKey = "UNIQUE_RANDOM",
            Quantity = -1,
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var pgEx = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal("23514", pgEx.SqlState); // check_violation
        Assert.Equal("CK_BoosterInventories_QuantityNonNegative", pgEx.ConstraintName);
    }
}

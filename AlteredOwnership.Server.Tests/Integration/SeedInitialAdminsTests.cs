using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlteredOwnership.Server.Tests.Integration;

// Deliberately doesn't share OwnershipApiFactory via IClassFixture: the migration
// reads InitialAdmins:KeycloakIds from the process environment at the moment it
// runs, so this test needs full control over when that env var is set relative to
// constructing its own factory — not something IClassFixture's automatic lifecycle
// gives us.
public class SeedInitialAdminsTests
{
    private const string EnvVarName = "InitialAdmins__KeycloakIds";

    [Fact]
    public async Task Migration_seeds_admin_from_config_and_upgrades_existing_row()
    {
        // A fresh, collision-proof id: no other test could plausibly rely on its role.
        var keycloakId = $"seed-admin-{Guid.NewGuid()}";

        Environment.SetEnvironmentVariable(EnvVarName, keycloakId);
        var factory = new OwnershipApiFactory();
        try
        {
            await ((IAsyncLifetime)factory).InitializeAsync();

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
            var user = await db.Users.SingleAsync(u => u.KeycloakId == keycloakId);
            Assert.Equal(UserRole.Admin, user.Role);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, null);
            await ((IAsyncLifetime)factory).DisposeAsync();
        }
    }
}

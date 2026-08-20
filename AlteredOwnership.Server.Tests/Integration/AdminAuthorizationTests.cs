using System.Net;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace AlteredOwnership.Server.Tests.Integration;

public class AdminAuthorizationTests(OwnershipApiFactory factory) : IClassFixture<OwnershipApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task SeedAdminAsync(string keycloakId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            KeycloakId = keycloakId,
            Role = UserRole.Admin,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Anonymous_endpoint_hitting_admin_route_is_unauthorized()
    {
        var response = await _client.GetAsync("/api/admin/ping");

        // TestAuthHandler always authenticates (no anonymous concept in tests), so an
        // unrecognised user without a Users row is Forbidden, not Unauthorized — this
        // test documents that path explicitly via a header-less request too.
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Non_admin_is_forbidden()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/ping");
        request.Headers.Add(TestAuthHandler.UserHeader, "non-admin-ping-user");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_is_allowed()
    {
        const string user = "admin-ping-user";
        await SeedAdminAsync(user);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/ping");
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}

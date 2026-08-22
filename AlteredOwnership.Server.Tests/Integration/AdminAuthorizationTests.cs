using System.Net;
using System.Net.Http.Json;
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

    private record MeResponse(string Sub, string? Pseudo, string? Email, string? Locale, bool IsAdmin);

    [Fact]
    public async Task Me_reports_is_admin_correctly()
    {
        const string admin = "me-is-admin-user";
        const string nonAdmin = "me-is-not-admin-user";
        await SeedAdminAsync(admin);

        using var adminReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        adminReq.Headers.Add(TestAuthHandler.UserHeader, admin);
        var adminMe = await (await _client.SendAsync(adminReq)).Content.ReadFromJsonAsync<MeResponse>();
        Assert.True(adminMe!.IsAdmin);

        using var nonAdminReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        nonAdminReq.Headers.Add(TestAuthHandler.UserHeader, nonAdmin);
        var nonAdminMe = await (await _client.SendAsync(nonAdminReq)).Content.ReadFromJsonAsync<MeResponse>();
        Assert.False(nonAdminMe!.IsAdmin);
    }
}

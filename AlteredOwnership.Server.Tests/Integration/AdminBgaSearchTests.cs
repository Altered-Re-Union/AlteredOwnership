using System.Net.Http.Json;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Infrastructure.Auth;
using AlteredOwnership.Server.Infrastructure.Bga;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AlteredOwnership.Server.Tests.Integration;

public class AdminBgaSearchTests : IClassFixture<OwnershipApiFactory>
{
    private record AdminUserSearchResult(string KeycloakId, string? Email, string? Pseudo);

    private const string AdminUser = "bga-search-admin";

    private readonly OwnershipApiFactory _factory;
    private readonly StubBgaApiClient _bgaApi = new StubBgaApiClient()
        .KnownPlayer("Shiranui_8668", "reunion-id-8668");
    private readonly StubKeycloakAdminClient _keycloak = new StubKeycloakAdminClient()
        .KnownUser("reunion-id-8668", email: "shiranui@example.com", pseudo: "Shiranui");
    private readonly HttpClient _client;

    public AdminBgaSearchTests(OwnershipApiFactory factory)
    {
        _factory = factory;
        _client = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBgaApiClient>();
            services.AddSingleton<IBgaApiClient>(_bgaApi);
            services.RemoveAll<IKeycloakAdminClient>();
            services.AddSingleton<IKeycloakAdminClient>(_keycloak);
        })).CreateClient();

        SeedAdminAsync(AdminUser).GetAwaiter().GetResult();
    }

    // AdminPolicy resolves the caller's role from the Users table (see
    // AdminAuthorizationHandler), not from any claim, so the test-auth header alone
    // doesn't pass it -- the caller needs an actual Admin row, same as AdminManagementTests.
    private async Task SeedAdminAsync(string keycloakId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        if (await db.Users.AnyAsync(u => u.KeycloakId == keycloakId)) return;

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
    public async Task Search_by_known_bga_name_resolves_to_the_Keycloak_profile()
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get, "/api/admin/users/search-by-bga?bgaName=Shiranui_8668");
        req.Headers.Add(TestAuthHandler.UserHeader, AdminUser);
        var response = await _client.SendAsync(req);

        var results = await response.Content.ReadFromJsonAsync<List<AdminUserSearchResult>>();
        var result = Assert.Single(results!);
        Assert.Equal("reunion-id-8668", result.KeycloakId);
        Assert.Equal("Shiranui", result.Pseudo);
        Assert.Equal("shiranui@example.com", result.Email);
    }

    [Fact]
    public async Task Search_by_unknown_bga_name_returns_an_empty_list()
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get, "/api/admin/users/search-by-bga?bgaName=NeverPlayed");
        req.Headers.Add(TestAuthHandler.UserHeader, AdminUser);
        var response = await _client.SendAsync(req);

        var results = await response.Content.ReadFromJsonAsync<List<AdminUserSearchResult>>();
        Assert.Empty(results!);
    }
}

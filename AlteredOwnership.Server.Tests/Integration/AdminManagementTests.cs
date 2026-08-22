using System.Net;
using System.Net.Http.Json;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Infrastructure.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AlteredOwnership.Server.Tests.Integration;

public class AdminManagementTests : IClassFixture<OwnershipApiFactory>
{
    private record CsrfResponse(string Token);
    private record AdminUserSearchResult(string KeycloakId, string? Email, string? Pseudo);

    private const string AdminUser = "admin-mgmt-admin";

    private readonly OwnershipApiFactory _factory;
    private readonly StubKeycloakAdminClient _keycloak = new StubKeycloakAdminClient()
        .KnownUser("admin-mgmt-target-a", email: "target-a@example.com", pseudo: "TargetA");
    private readonly HttpClient _client;

    public AdminManagementTests(OwnershipApiFactory factory)
    {
        _factory = factory;
        _client = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IKeycloakAdminClient>();
            services.AddSingleton<IKeycloakAdminClient>(_keycloak);
        })).CreateClient();

        SeedAdminAsync(AdminUser).GetAwaiter().GetResult();
    }

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

    private async Task<string> FetchCsrfAsync()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        req.Headers.Add(TestAuthHandler.UserHeader, AdminUser);
        using var res = await _client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<CsrfResponse>())!.Token;
    }

    private async Task<HttpResponseMessage> SetRoleAsync(string keycloakId, UserRole role)
    {
        var token = await FetchCsrfAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{keycloakId}/role")
        {
            Content = JsonContent.Create(new { role = role.ToString() }),
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Headers.Add(TestAuthHandler.UserHeader, AdminUser);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Promoting_then_listing_shows_the_new_admin()
    {
        var promote = await SetRoleAsync("admin-mgmt-target-a", UserRole.Admin);
        Assert.Equal(HttpStatusCode.NoContent, promote.StatusCode);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/admins");
        req.Headers.Add(TestAuthHandler.UserHeader, AdminUser);
        var admins = await (await _client.SendAsync(req)).Content.ReadFromJsonAsync<List<AdminUserSearchResult>>();

        var target = admins!.Single(a => a.KeycloakId == "admin-mgmt-target-a");
        Assert.Equal("TargetA", target.Pseudo);
        Assert.Equal("target-a@example.com", target.Email);
    }

    [Fact]
    public async Task Demoting_an_admin_removes_them_from_the_list()
    {
        const string keycloakId = "admin-mgmt-target-demote";
        _keycloak.KnownUser(keycloakId, pseudo: "ToDemote");
        Assert.Equal(HttpStatusCode.NoContent, (await SetRoleAsync(keycloakId, UserRole.Admin)).StatusCode);

        var demote = await SetRoleAsync(keycloakId, UserRole.Player);
        Assert.Equal(HttpStatusCode.NoContent, demote.StatusCode);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/admins");
        req.Headers.Add(TestAuthHandler.UserHeader, AdminUser);
        var admins = await (await _client.SendAsync(req)).Content.ReadFromJsonAsync<List<AdminUserSearchResult>>();

        Assert.DoesNotContain(admins!, a => a.KeycloakId == keycloakId);
    }

    [Fact]
    public async Task Cannot_demote_the_last_remaining_admin()
    {
        // A fully standalone factory (own Postgres container), not the shared _factory:
        // this test needs the Users table to contain EXACTLY one admin, which would be
        // impossible to guarantee (or would break sibling tests) against a fixture other
        // tests in this class also seed admins into.
        var factory = new OwnershipApiFactory();
        try
        {
            await ((IAsyncLifetime)factory).InitializeAsync();

            const string soleAdmin = "admin-mgmt-sole-admin";
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    KeycloakId = soleAdmin,
                    Role = UserRole.Admin,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            var client = factory.CreateClient();

            using var csrfReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
            csrfReq.Headers.Add(TestAuthHandler.UserHeader, soleAdmin);
            var token = (await (await client.SendAsync(csrfReq)).Content.ReadFromJsonAsync<CsrfResponse>())!.Token;

            using var demoteReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{soleAdmin}/role")
            {
                Content = JsonContent.Create(new { role = "Player" }),
            };
            demoteReq.Headers.Add("X-CSRF-TOKEN", token);
            demoteReq.Headers.Add(TestAuthHandler.UserHeader, soleAdmin);
            var response = await client.SendAsync(demoteReq);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            using var scope2 = factory.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<OwnershipDbContext>();
            var user = await db2.Users.SingleAsync(u => u.KeycloakId == soleAdmin);
            Assert.Equal(UserRole.Admin, user.Role);
        }
        finally
        {
            await ((IAsyncLifetime)factory).DisposeAsync();
        }
    }
}

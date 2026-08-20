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

public class AdminRewardsTests : IClassFixture<OwnershipApiFactory>
{
    private record CsrfResponse(string Token);
    private record CardOwnershipResponse(string Reference, int Quantity);

    private const string AdminUser = "admin-rewards-admin";

    private readonly OwnershipApiFactory _factory;
    private readonly StubKeycloakAdminClient _keycloak = new StubKeycloakAdminClient()
        .KnownUser("reward-target-a", email: "a@example.com", pseudo: "PlayerA")
        .KnownUser("reward-target-b", email: "b@example.com", pseudo: "PlayerB");
    private readonly HttpClient _client;

    public AdminRewardsTests(OwnershipApiFactory factory)
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

    private async Task SeedStockAsync(params (string Reference, string Set, bool IsDistributed)[] rows)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        foreach (var row in rows)
        {
            db.UniqueCardStock.Add(new UniqueCardStock
            {
                CardReference = row.Reference,
                Set = row.Set,
                Faction = "AX",
                IsDistributed = row.IsDistributed,
            });
        }
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

    private async Task<HttpResponseMessage> PostAdminAsync(string path, object body)
    {
        var token = await FetchCsrfAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Headers.Add(TestAuthHandler.UserHeader, AdminUser);
        return await _client.SendAsync(request);
    }

    private async Task<CardOwnershipResponse[]> GetCollectionAsync(string keycloakId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/collection");
        req.Headers.Add(TestAuthHandler.UserHeader, keycloakId);
        using var res = await _client.SendAsync(req);
        return (await res.Content.ReadFromJsonAsync<CardOwnershipResponse[]>())!;
    }

    [Fact]
    public async Task Give_specific_card_to_multiple_targets_succeeds()
    {
        var response = await PostAdminAsync("/api/admin/rewards/card", new
        {
            cardReference = "ALT_ALIZE_B_AX_60_C",
            quantity = 3,
            acquiredFrom = "Test event",
            keycloakUserIds = new[] { "reward-target-a", "reward-target-b" },
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var collectionA = await GetCollectionAsync("reward-target-a");
        var collectionB = await GetCollectionAsync("reward-target-b");
        Assert.Equal(3, collectionA.Single(c => c.Reference == "ALT_ALIZE_B_AX_60_C").Quantity);
        Assert.Equal(3, collectionB.Single(c => c.Reference == "ALT_ALIZE_B_AX_60_C").Quantity);
    }

    [Fact]
    public async Task Unique_card_with_quantity_other_than_one_is_rejected()
    {
        var response = await PostAdminAsync("/api/admin/rewards/card", new
        {
            cardReference = "ALT_ALIZE_B_AX_61_U_1",
            quantity = 2,
            acquiredFrom = "Test event",
            keycloakUserIds = new[] { "reward-target-a" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Giving_same_unique_to_two_targets_grants_nobody()
    {
        var response = await PostAdminAsync("/api/admin/rewards/card", new
        {
            cardReference = "ALT_ALIZE_B_AX_62_U_2",
            quantity = 1,
            acquiredFrom = "Test event",
            keycloakUserIds = new[] { "reward-target-a", "reward-target-b" },
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Nothing was granted", body);

        // All-or-nothing: target-a would have succeeded on its own, but since
        // target-b's grant conflicted, neither of them actually got the card.
        var collectionA = await GetCollectionAsync("reward-target-a");
        var collectionB = await GetCollectionAsync("reward-target-b");
        Assert.DoesNotContain(collectionA, c => c.Reference == "ALT_ALIZE_B_AX_62_U_2");
        Assert.DoesNotContain(collectionB, c => c.Reference == "ALT_ALIZE_B_AX_62_U_2");
    }

    [Fact]
    public async Task Unknown_keycloak_id_aborts_before_granting_to_anyone()
    {
        var response = await PostAdminAsync("/api/admin/rewards/card", new
        {
            cardReference = "ALT_ALIZE_B_AX_63_C",
            quantity = 1,
            acquiredFrom = "Test event",
            keycloakUserIds = new[] { "reward-target-a", "unknown-keycloak-id" },
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Nothing was granted", body);

        var collectionA = await GetCollectionAsync("reward-target-a");
        Assert.DoesNotContain(collectionA, c => c.Reference == "ALT_ALIZE_B_AX_63_C");
    }

    [Fact]
    public async Task Random_unique_reward_respects_set_filter_marks_distributed_and_exhausts()
    {
        await SeedStockAsync(
            ("ALT_SETA_B_AX_01_U_1", "SETA", false),
            ("ALT_SETA_B_AX_02_U_2", "SETA", false),
            ("ALT_SETA_B_AX_03_U_3", "SETA", true), // already distributed, must never be picked
            ("ALT_SETB_B_AX_01_U_4", "SETB", false));

        var response = await PostAdminAsync("/api/admin/rewards/random-unique", new
        {
            set = "SETA",
            quantity = 2,
            acquiredFrom = "Test event",
            keycloakUserIds = new[] { "reward-target-a" },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var granted = (await response.Content.ReadFromJsonAsync<Dictionary<string, List<string>>>())!;
        var references = granted["reward-target-a"];
        Assert.Equal(2, references.Count);
        Assert.All(references, r => Assert.StartsWith("ALT_SETA", r));
        Assert.DoesNotContain("ALT_SETA_B_AX_03_U_3", references);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        Assert.True(await db.UniqueCardStock.AllAsync(s => s.Set != "SETA" || s.IsDistributed));

        // Stock for SETA is now exhausted.
        var exhaustedResponse = await PostAdminAsync("/api/admin/rewards/random-unique", new
        {
            set = "SETA",
            quantity = 1,
            acquiredFrom = "Test event",
            keycloakUserIds = new[] { "reward-target-b" },
        });
        Assert.Equal(HttpStatusCode.Conflict, exhaustedResponse.StatusCode);
        var body = await exhaustedResponse.Content.ReadAsStringAsync();
        Assert.Contains("Nothing was granted", body);
    }

    [Fact]
    public async Task Random_unique_reward_without_set_filter_draws_and_marks_distributed()
    {
        // Deliberately doesn't rely on sweeping/counting the whole table (which would
        // be prohibitively expensive against the real ~5.4M-row production seed) —
        // just checks the specific rows this call actually touched.
        await SeedStockAsync(
            ("ALT_NOFILTA_B_AX_01_U_1", "NOFILTA", false),
            ("ALT_NOFILTB_B_AX_01_U_2", "NOFILTB", false));

        var response = await PostAdminAsync("/api/admin/rewards/random-unique", new
        {
            set = (string?)null,
            quantity = 2,
            acquiredFrom = "Test event",
            keycloakUserIds = new[] { "reward-target-a" },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var granted = (await response.Content.ReadFromJsonAsync<Dictionary<string, List<string>>>())!;
        var references = granted["reward-target-a"];
        Assert.Equal(2, references.Count);
        Assert.Equal(2, references.Distinct().Count());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        Assert.True(await db.UniqueCardStock
            .Where(s => references.Contains(s.CardReference))
            .AllAsync(s => s.IsDistributed));
    }

    [Fact]
    public async Task Random_unique_reward_insufficient_stock_across_targets_grants_nobody()
    {
        // Only 1 available, but 2 targets each asking for 1: total demand outruns
        // supply, so the whole batch — including the first target's draw, which would
        // have succeeded on its own — must roll back.
        await SeedStockAsync(("ALT_SCARCE_B_AX_01_U_1", "SCARCE", false));

        var response = await PostAdminAsync("/api/admin/rewards/random-unique", new
        {
            set = "SCARCE",
            quantity = 1,
            acquiredFrom = "Test event",
            keycloakUserIds = new[] { "reward-target-a", "reward-target-b" },
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var collectionA = await GetCollectionAsync("reward-target-a");
        Assert.DoesNotContain(collectionA, c => c.Reference == "ALT_SCARCE_B_AX_01_U_1");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        var row = await db.UniqueCardStock.SingleAsync(s => s.CardReference == "ALT_SCARCE_B_AX_01_U_1");
        Assert.False(row.IsDistributed);
    }
}

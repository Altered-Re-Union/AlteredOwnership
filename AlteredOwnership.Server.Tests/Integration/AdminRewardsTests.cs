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
    private record BoosterInventoryResponse(string BoosterTypeKey, string Name, string? ImagePath, int Quantity);
    private record EventSummaryResponse(long Id, string Name, string Kind, int CardsReceived, int CardsGiven, int BoostersReceived, int BoostersGiven);

    private const string AdminUser = "admin-rewards-admin";

    private readonly OwnershipApiFactory _factory;
    private readonly StubKeycloakAdminClient _keycloak = new StubKeycloakAdminClient()
        .KnownUser("reward-target-a", email: "a@example.com", pseudo: "PlayerA")
        .KnownUser("reward-target-b", email: "b@example.com", pseudo: "PlayerB")
        .KnownUser("reward-target-booster", email: "c@example.com", pseudo: "PlayerC")
        .KnownUser("reward-target-mixed", email: "d@example.com", pseudo: "PlayerD");
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

    private async Task<string> FetchCsrfAsync()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        req.Headers.Add(TestAuthHandler.UserHeader, AdminUser);
        using var res = await _client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<CsrfResponse>())!.Token;
    }

    private async Task<HttpResponseMessage> PostRewardAsync(object body)
    {
        var token = await FetchCsrfAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/rewards") { Content = JsonContent.Create(body) };
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

    private async Task<List<BoosterInventoryResponse>> GetBoostersAsync(string keycloakId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/boosters");
        req.Headers.Add(TestAuthHandler.UserHeader, keycloakId);
        using var res = await _client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<List<BoosterInventoryResponse>>())!;
    }

    private async Task<List<EventSummaryResponse>> GetHistoryAsync(string keycloakId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/history");
        req.Headers.Add(TestAuthHandler.UserHeader, keycloakId);
        using var res = await _client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<List<EventSummaryResponse>>())!;
    }

    [Fact]
    public async Task Give_specific_card_to_multiple_targets_succeeds()
    {
        var response = await PostRewardAsync(new
        {
            acquiredFrom = "Test event",
            keycloakUserIds = new[] { "reward-target-a", "reward-target-b" },
            cards = new[] { new { cardReference = "ALT_ALIZE_B_AX_60_C", quantity = 3 } },
            boosters = Array.Empty<object>(),
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
        var response = await PostRewardAsync(new
        {
            acquiredFrom = "Test event",
            keycloakUserIds = new[] { "reward-target-a" },
            cards = new[] { new { cardReference = "ALT_ALIZE_B_AX_61_U_1", quantity = 2 } },
            boosters = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Giving_same_unique_to_two_targets_grants_nobody()
    {
        var response = await PostRewardAsync(new
        {
            acquiredFrom = "Test event",
            keycloakUserIds = new[] { "reward-target-a", "reward-target-b" },
            cards = new[] { new { cardReference = "ALT_ALIZE_B_AX_62_U_2", quantity = 1 } },
            boosters = Array.Empty<object>(),
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
        var response = await PostRewardAsync(new
        {
            acquiredFrom = "Test event",
            keycloakUserIds = new[] { "reward-target-a", "unknown-keycloak-id" },
            cards = new[] { new { cardReference = "ALT_ALIZE_B_AX_63_C", quantity = 1 } },
            boosters = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Nothing was granted", body);

        var collectionA = await GetCollectionAsync("reward-target-a");
        Assert.DoesNotContain(collectionA, c => c.Reference == "ALT_ALIZE_B_AX_63_C");
    }

    [Fact]
    public async Task Unknown_booster_type_is_rejected()
    {
        var response = await PostRewardAsync(new
        {
            acquiredFrom = "Test event",
            keycloakUserIds = new[] { "reward-target-a" },
            cards = Array.Empty<object>(),
            boosters = new[] { new { boosterTypeKey = "NOT_A_REAL_TYPE", quantity = 1 } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Booster_grant_adds_to_the_targets_inventory_without_resolving_a_card()
    {
        var response = await PostRewardAsync(new
        {
            acquiredFrom = "Test event",
            keycloakUserIds = new[] { "reward-target-booster" },
            cards = Array.Empty<object>(),
            boosters = new[] { new { boosterTypeKey = "UNIQUE_RANDOM", quantity = 3 } },
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var inventory = await GetBoostersAsync("reward-target-booster");
        Assert.Equal(3, inventory.Single(b => b.BoosterTypeKey == "UNIQUE_RANDOM").Quantity);
    }

    [Fact]
    public async Task Cards_and_boosters_in_one_request_produce_a_single_history_event()
    {
        var response = await PostRewardAsync(new
        {
            acquiredFrom = "Convention 2026",
            keycloakUserIds = new[] { "reward-target-mixed" },
            cards = new[]
            {
                new { cardReference = "ALT_ALIZE_B_AX_64_C", quantity = 2 },
                new { cardReference = "ALT_ALIZE_B_AX_65_C", quantity = 1 },
            },
            boosters = new[] { new { boosterTypeKey = "UNIQUE_RANDOM", quantity = 2 } },
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var events = await GetHistoryAsync("reward-target-mixed");
        var evt = Assert.Single(events);
        Assert.Equal("Convention 2026", evt.Name);
        Assert.Equal(3, evt.CardsReceived); // 2 + 1 cards
        Assert.Equal(0, evt.CardsGiven);
        Assert.Equal(2, evt.BoostersReceived);
        Assert.Equal(0, evt.BoostersGiven);
    }
}

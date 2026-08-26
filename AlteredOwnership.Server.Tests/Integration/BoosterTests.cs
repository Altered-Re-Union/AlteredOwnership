using System.Net;
using System.Net.Http.Json;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlteredOwnership.Server.Tests.Integration;

public class BoosterTests(OwnershipApiFactory factory) : IClassFixture<OwnershipApiFactory>
{
    private record CsrfDto(string Token);
    private record OpenedCardDto(string CardReference, string? Name, string? ImagePath, bool IsUnique);
    private record BoosterInventoryDto(string BoosterTypeKey, string Name, string? ImagePath, int Quantity);
    private record CardOwnershipDto(string Reference, int Quantity);
    private record EventSummaryDto(long Id, string Name, string Kind, int CardsReceived, int CardsGiven, int BoostersReceived, int BoostersGiven);

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> SeedUserAsync(string keycloakId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        var user = new User { Id = Guid.NewGuid(), KeycloakId = keycloakId, CreatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task GrantBoosterAsync(Guid userId, string boosterTypeKey, int quantity)
    {
        using var scope = factory.Services.CreateScope();
        var rewards = scope.ServiceProvider.GetRequiredService<RewardService>();
        await rewards.RewardBatchAsync([userId], [], [(boosterTypeKey, quantity)], "Test grant", CancellationToken.None);
    }

    private async Task SeedStockAsync(params (string Reference, string Set, string Faction, bool IsDistributed)[] rows)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        foreach (var row in rows)
        {
            db.UniqueCardStock.Add(new UniqueCardStock
            {
                CardReference = row.Reference,
                Set = row.Set,
                Faction = row.Faction,
                IsDistributed = row.IsDistributed,
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task<string> FetchCsrfAsync(string keycloakId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        req.Headers.Add(TestAuthHandler.UserHeader, keycloakId);
        using var res = await _client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<CsrfDto>())!.Token;
    }

    private async Task<HttpResponseMessage> OpenBoosterAsync(string keycloakId, string boosterTypeKey, int quantity = 1)
    {
        var token = await FetchCsrfAsync(keycloakId);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/boosters/{boosterTypeKey}/open")
        {
            Content = JsonContent.Create(new { quantity }),
        };
        req.Headers.Add("X-CSRF-TOKEN", token);
        req.Headers.Add(TestAuthHandler.UserHeader, keycloakId);
        return await _client.SendAsync(req);
    }

    private async Task<List<BoosterInventoryDto>> GetInventoryAsync(string keycloakId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/boosters");
        req.Headers.Add(TestAuthHandler.UserHeader, keycloakId);
        using var res = await _client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<List<BoosterInventoryDto>>())!;
    }

    private async Task<List<CardOwnershipDto>> GetCollectionAsync(string keycloakId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/collection");
        req.Headers.Add(TestAuthHandler.UserHeader, keycloakId);
        using var res = await _client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<List<CardOwnershipDto>>())!;
    }

    private async Task<List<EventSummaryDto>> GetHistoryAsync(string keycloakId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/history");
        req.Headers.Add(TestAuthHandler.UserHeader, keycloakId);
        using var res = await _client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<List<EventSummaryDto>>())!;
    }

    [Fact]
    public async Task Opening_a_booster_draws_from_its_scoped_stock_and_updates_inventory_and_collection()
    {
        const string keycloakId = "booster-open-user";
        var userId = await SeedUserAsync(keycloakId);
        await SeedStockAsync(
            ("ALT_BOOST_B_AX_01_U_1", "SETX", "AX", false),
            ("ALT_BOOST_B_BR_01_U_2", "SETX", "BR", false)); // wrong faction, must never be picked
        await GrantBoosterAsync(userId, "UNIQUE_RANDOM_AXIOM", 2);

        var before = await GetInventoryAsync(keycloakId);
        Assert.Equal(2, before.Single(b => b.BoosterTypeKey == "UNIQUE_RANDOM_AXIOM").Quantity);

        var response = await OpenBoosterAsync(keycloakId, "UNIQUE_RANDOM_AXIOM");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var opened = (await response.Content.ReadFromJsonAsync<List<OpenedCardDto>>())!;
        var card = Assert.Single(opened);
        Assert.Equal("ALT_BOOST_B_AX_01_U_1", card.CardReference);
        Assert.True(card.IsUnique);

        var after = await GetInventoryAsync(keycloakId);
        Assert.Equal(1, after.Single(b => b.BoosterTypeKey == "UNIQUE_RANDOM_AXIOM").Quantity);

        var collection = await GetCollectionAsync(keycloakId);
        Assert.Equal(1, collection.Single(c => c.Reference == "ALT_BOOST_B_AX_01_U_1").Quantity);
    }

    [Fact]
    public async Task Opening_the_last_booster_succeeds_and_removes_it_from_inventory()
    {
        // The exact 1->0 boundary, as opposed to Opening_more_boosters_than_owned_fails_and_
        // grants_nothing below (1 owned, 2 requested) — this proves the non-negative guard
        // doesn't also reject the legitimate last-copy case. ProjectionReconciler deletes the
        // row outright at quantity 0 rather than keeping a zero row, so success here shows up
        // as the booster type being absent from the inventory list, not Quantity == 0.
        const string keycloakId = "booster-lastopen-user";
        var userId = await SeedUserAsync(keycloakId);
        await SeedStockAsync(("ALT_LASTOPEN_B_AX_01_U_1", "SETX", "AX", false));
        await GrantBoosterAsync(userId, "UNIQUE_RANDOM_AXIOM", 1);

        var response = await OpenBoosterAsync(keycloakId, "UNIQUE_RANDOM_AXIOM");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var inventory = await GetInventoryAsync(keycloakId);
        Assert.DoesNotContain(inventory, b => b.BoosterTypeKey == "UNIQUE_RANDOM_AXIOM");
    }

    [Fact]
    public async Task Opening_more_boosters_than_owned_fails_and_grants_nothing()
    {
        const string keycloakId = "booster-overopen-user";
        var userId = await SeedUserAsync(keycloakId);
        // Enough card stock for both draws to succeed, so the failure below is
        // provably the booster-inventory check, not exhausted card stock.
        await SeedStockAsync(
            ("ALT_OVEROPEN_B_AX_01_U_1", "SETX", "AX", false),
            ("ALT_OVEROPEN_B_AX_02_U_2", "SETX", "AX", false));
        await GrantBoosterAsync(userId, "UNIQUE_RANDOM_AXIOM", 1);

        var response = await OpenBoosterAsync(keycloakId, "UNIQUE_RANDOM_AXIOM", quantity: 2);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var inventory = await GetInventoryAsync(keycloakId);
        Assert.Equal(1, inventory.Single(b => b.BoosterTypeKey == "UNIQUE_RANDOM_AXIOM").Quantity);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        Assert.True(await db.UniqueCardStock
            .Where(s => s.CardReference.StartsWith("ALT_OVEROPEN"))
            .AllAsync(s => !s.IsDistributed));
    }

    [Fact]
    public async Task Opening_with_no_stock_in_scope_fails()
    {
        // A faction no other test in this class seeds stock for — this class shares
        // one database across all its tests, so reusing the AX scope here could pick
        // up a leftover undistributed row from a sibling test instead of proving
        // "no stock" behavior.
        const string keycloakId = "booster-nostock-user";
        var userId = await SeedUserAsync(keycloakId);
        await GrantBoosterAsync(userId, "UNIQUE_RANDOM_YZMIR", 1);

        var response = await OpenBoosterAsync(keycloakId, "UNIQUE_RANDOM_YZMIR");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Opening_an_unknown_booster_type_returns_not_found()
    {
        const string keycloakId = "booster-unknown-user";
        await SeedUserAsync(keycloakId);

        var response = await OpenBoosterAsync(keycloakId, "NOT_A_REAL_TYPE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Opening_records_a_single_history_event_distinct_from_the_grant()
    {
        const string keycloakId = "booster-history-user";
        var userId = await SeedUserAsync(keycloakId);
        await SeedStockAsync(("ALT_BOOSTHIST_B_AX_01_U_1", "SETX", "AX", false));
        await GrantBoosterAsync(userId, "UNIQUE_RANDOM_AXIOM", 1);

        await OpenBoosterAsync(keycloakId, "UNIQUE_RANDOM_AXIOM");

        var events = await GetHistoryAsync(keycloakId);
        Assert.Equal(2, events.Count); // one for the grant, one for the open
        var openEvent = events.Single(e => e.Name == "Ouverture de booster : Unique aléatoire Axiom");
        Assert.Equal("BoosterOpened", openEvent.Kind);
        Assert.Equal(1, openEvent.CardsReceived);
        Assert.Equal(0, openEvent.CardsGiven);
        Assert.Equal(0, openEvent.BoostersReceived);
        Assert.Equal(1, openEvent.BoostersGiven);
    }
}

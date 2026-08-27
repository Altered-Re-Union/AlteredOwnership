using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlteredOwnership.Server.Tests.Integration;

// Covers UniqueStockService.ReserveRandomAsync directly (BoosterTests only exercises it
// indirectly through the HTTP endpoint) — this is where the anchor/wraparound query
// logic that replaced `ORDER BY random()` (see UniqueStockService for why) actually
// needs proving: every candidate gets picked exactly once, filters are respected, and
// concurrent draws against the same pool never double-claim a row.
public class UniqueStockServiceTests(OwnershipApiFactory factory) : IClassFixture<OwnershipApiFactory>
{
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

    private async Task<string> ReserveAsync(string? set, string? faction)
    {
        using var scope = factory.Services.CreateScope();
        var stock = scope.ServiceProvider.GetRequiredService<UniqueStockService>();
        return await stock.ReserveRandomAsync(set, faction, CancellationToken.None);
    }

    [Fact]
    public async Task Reserving_claims_a_matching_row_and_marks_it_distributed()
    {
        await SeedStockAsync(
            ("ALT_USSVC_B_AX_01_U_1", "SETX", "AX", false),
            ("ALT_USSVC_B_BR_01_U_2", "SETX", "BR", false)); // wrong faction, must never be picked

        var reference = await ReserveAsync("SETX", "AX");
        Assert.Equal("ALT_USSVC_B_AX_01_U_1", reference);

        using var readScope = factory.Services.CreateScope();
        var db = readScope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        var claimed = await db.UniqueCardStock.SingleAsync(s => s.CardReference == "ALT_USSVC_B_AX_01_U_1");
        Assert.True(claimed.IsDistributed);
        var untouched = await db.UniqueCardStock.SingleAsync(s => s.CardReference == "ALT_USSVC_B_BR_01_U_2");
        Assert.False(untouched.IsDistributed);
    }

    [Fact]
    public async Task Reserving_with_no_filters_draws_from_the_whole_pool()
    {
        // Unlike the other tests here, an unfiltered draw can legitimately land on any
        // undistributed row in the shared database this test class's fixture uses — not
        // necessarily the one seeded below (a sibling test's leftover row is just as
        // valid a match) — so this only asserts on what's true regardless of which row
        // gets picked: it succeeds, and whatever it returns is now marked distributed.
        await SeedStockAsync(("ALT_USSVCANY_B_AX_01_U_1", "SETY", "AX", false));

        var reference = await ReserveAsync(null, null);

        using var readScope = factory.Services.CreateScope();
        var db = readScope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        var claimed = await db.UniqueCardStock.SingleAsync(s => s.CardReference == reference);
        Assert.True(claimed.IsDistributed);
    }

    [Fact]
    public async Task Reserving_every_row_in_a_pool_returns_each_exactly_once()
    {
        // Exercises both the forward (>= anchor) and wraparound (< anchor) branches of
        // ReserveRandomAsync across repeated draws from the same shrinking pool — proves
        // the two-query split still covers 100% of the pool, not just "some" of it.
        var expected = Enumerable.Range(1, 25).Select(i => $"ALT_USSVCALL_B_AX_{i:00}_U_{i}").ToList();
        await SeedStockAsync(expected.Select(r => (r, "SETZ", "AX", false)).ToArray());

        var drawn = new List<string>();
        for (var i = 0; i < expected.Count; i++)
            drawn.Add(await ReserveAsync("SETZ", "AX"));

        Assert.Equal(expected.OrderBy(r => r), drawn.OrderBy(r => r));

        await Assert.ThrowsAsync<NoUniqueStockAvailableException>(() => ReserveAsync("SETZ", "AX"));
    }

    [Fact]
    public async Task Reserving_with_no_stock_in_scope_throws()
    {
        await SeedStockAsync(("ALT_USSVCEMPTY_B_AX_01_U_1", "SETW", "AX", true)); // already distributed

        await Assert.ThrowsAsync<NoUniqueStockAvailableException>(() => ReserveAsync("SETW", "AX"));
    }

    [Fact]
    public async Task Concurrent_reservations_against_the_same_pool_never_double_claim_a_row()
    {
        const int poolSize = 20;
        var references = Enumerable.Range(1, poolSize).Select(i => $"ALT_USSVCCONC_B_AX_{i:00}_U_{i}").ToList();
        await SeedStockAsync(references.Select(r => (r, "SETC", "AX", false)).ToArray());

        var draws = await Task.WhenAll(Enumerable.Range(0, poolSize).Select(_ => ReserveAsync("SETC", "AX")));

        Assert.Equal(poolSize, draws.Distinct().Count());
        Assert.Equal(references.OrderBy(r => r), draws.OrderBy(r => r));
    }
}

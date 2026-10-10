using Xunit.Abstractions;
using System.Diagnostics;
using System.Data.Common;
using System.Text;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain;
using AlteredOwnership.Server.Domain.Services;
using AlteredOwnership.Server.Endpoints;
using AlteredOwnership.Server.Tests.Integration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace AlteredOwnership.Server.Tests.Benchmarks;

// Not a regression test: a measurement campaign for POST /api/alt-arts/apply-to-deck.
// Run with:  dotnet test --filter "Category=Bench" --logger "console;verbosity=detailed"
// Results are also written to bench-results.txt next to the test binaries.
//
// Real CardArtCatalog (seeded by migrations) + fake users / ownerships / preferences
// loaded in bulk. Every alternative strategy is checked line-for-line against the
// current implementation before its timings are reported.
[Trait("Category", "Bench")]
public class ApplyToDeckBenchmark(OwnershipApiFactory factory, ITestOutputHelper output)
    : IClassFixture<OwnershipApiFactory>
{
    private const int UserCount = 1500;
    private const int OwnedPerUser = 400;
    private const int GlobalPrefFamiliesPerUser = 120;
    private const int Runs = 300;
    private const int DeckLines = 45;

    private readonly StringBuilder _report = new();

    private void Log(string s)
    {
        output.WriteLine(s);
        _report.AppendLine(s);
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Interlocked.Increment(ref Count); return base.ReaderExecutingAsync(command, eventData, result, ct); }
    }

    private record Deck(string KeycloakId, bool Global, List<OwnershipCheckItem> Lines);

    [Fact]
    public async Task Run()
    {
        var counter = new QueryCounter();
        var f = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IInterceptor>(counter)));

        List<CardArtCatalogEntry> catalog;
        using (var scope = f.Services.CreateScope())
            catalog = await scope.ServiceProvider.GetRequiredService<OwnershipDbContext>()
                .CardArtCatalog.AsNoTracking().ToListAsync();
        Log($"Catalog rows: {catalog.Count}");

        var rnd = new Random(42);
        await SeedAsync(f, catalog, rnd);
        var decks = BuildDecks(catalog, rnd);

        // --- Strategy 0: current code -------------------------------------------------
        var baseline = new Strategy("S0 production code (snapshot + 3 queries)", async (db, d) =>
        {
            var userId = await db.Users.Where(u => u.KeycloakId == d.KeycloakId).Select(u => u.Id).FirstAsync();
            return (await new AltArtService(db, f.Services.GetRequiredService<CardArtCatalogCache>()).ApplyToDeckAsync(userId, d.Lines, default)).Lines
                .Select(l => l.Select(i => (i.Reference, i.Quantity)).ToList()).ToList();
        });

        // --- Strategy 1: in-memory catalog snapshot -----------------------------------
        CatalogSnapshot? snapshot = null;
        var s1 = new Strategy("S1 catalog snapshot in memory", async (db, d) =>
        {
            snapshot ??= await CatalogSnapshot.LoadAsync(db);
            var (userId, mode) = await db.Users.Where(u => u.KeycloakId == d.KeycloakId)
                .Select(u => new ValueTuple<Guid, AltArtPreferenceMode>(u.Id, u.AltArtPreferenceMode)).FirstAsync();
            var tracked = d.Lines.Select(i => snapshot.ByRef.GetValueOrDefault(i.Reference))
                .Where(e => e is not null && !snapshot.IsInfinite(e)).Select(e => e!.Reference).Distinct().ToList();
            var owned = tracked.Count == 0 ? new() : await db.CardOwnerships
                .Where(o => o.UserId == userId && tracked.Contains(o.CardReference)).AsNoTracking()
                .ToDictionaryAsync(o => o.CardReference, o => o.Quantity);
            // Tokens still come from the user's preferences (1 query), typed via the snapshot.
            var prefs = await db.UserCardArtPreferences.Where(p => p.UserId == userId).AsNoTracking().ToListAsync();
            _ = prefs.Where(p => snapshot.ByRef.GetValueOrDefault(p.PreferredReference)?.IsTokenEntry() == true).ToList();
            return PerDeck.Resolve(d.Lines, e => snapshot.ByRef.GetValueOrDefault(e), snapshot, owned);
        });

        // --- Strategy 2/3: denormalised columns (DefaultReference, IsInfinite) --------
        await using (var scope = f.Services.CreateAsyncScope())
            await Denormalize(scope.ServiceProvider.GetRequiredService<OwnershipDbContext>(), catalog);

        var s2 = new Strategy("S2 denormalised: 1 join query + user + tokens", async (db, d) =>
        {
            var userId = await db.Users.Where(u => u.KeycloakId == d.KeycloakId).Select(u => u.Id).FirstAsync();
            var refs = d.Lines.Select(i => i.Reference).Distinct().ToArray();
            var rows = await db.Database.SqlQueryRaw<JoinRow>(JoinSql(byUserId: true),
                new NpgsqlParameter("u", userId), new NpgsqlParameter("refs", refs)).ToListAsync();
            await LoadTokenPrefs(db, userId);
            return PerDeck.ResolveFromRows(d.Lines, rows);
        });

        var s3 = new Strategy("S3 denormalised: user+catalog+ownership in ONE query (+tokens)", async (db, d) =>
        {
            var refs = d.Lines.Select(i => i.Reference).Distinct().ToArray();
            var rows = await db.Database.SqlQueryRaw<JoinRow>(JoinSql(byUserId: false),
                new NpgsqlParameter("u", d.KeycloakId), new NpgsqlParameter("refs", refs)).ToListAsync();
            var userId = rows[0].UserId;
            await LoadTokenPrefs(db, userId);
            return PerDeck.ResolveFromRows(d.Lines, rows);
        });

        // --- Run ---------------------------------------------------------------------
        foreach (var (label, subset) in new[]
        {
            ("PerDeck users", decks.Where(d => !d.Global).Take(Runs).ToList()),
        })
        {
            Log($"\n=== {label}: {subset.Count} requests, {DeckLines}-line decks ===");
            var expected = new List<List<List<(string, int)>>>();
            foreach (var s in new[] { baseline, s1, s2, s3 })
                await Measure(f, counter, s, subset, s == baseline ? expected : null, expected);
        }

        // Global mode: only the current code (the user wants to drop it; this quantifies the cost).
        {
            var subset = decks.Where(d => d.Global).Take(Runs).ToList();
            Log($"\n=== Global-mode users (current code only): {subset.Count} requests ===");
            await Measure(f, counter, baseline, subset, null, null);
        }

        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "bench-results.txt"), _report.ToString());
    }

    private record Strategy(string Name, Func<OwnershipDbContext, Deck, Task<List<List<(string, int)>>>> Run);

    private async Task Measure(WebApplicationFactory<Program> f, QueryCounter counter, Strategy s, List<Deck> decks,
        List<List<List<(string, int)>>>? capture, List<List<List<(string, int)>>>? compareTo)
    {
        var times = new List<double>();
        var queries = new List<int>();
        double cold = 0;
        var mismatches = 0;
        for (var i = 0; i < decks.Count; i++)
        {
            // New scope per request, like production (fresh DbContext, pooled connection).
            await using var scope = f.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
            var before = counter.Count;
            var sw = Stopwatch.StartNew();
            var result = await s.Run(db, decks[i]);
            sw.Stop();
            var ms = sw.Elapsed.TotalMilliseconds;
            if (i == 0) cold = ms; else { times.Add(ms); queries.Add(counter.Count - before); }
            if (capture is not null) capture.Add(result);
            else if (compareTo is not null && !Same(result, compareTo[i])) mismatches++;
        }
        times.Sort();
        double P(double p) => times[(int)Math.Min(times.Count - 1, p * times.Count)];
        Log($"{s.Name,-70} cold={cold,7:F1}ms  p50={P(.5),6:F2}  p95={P(.95),6:F2}  max={times[^1],6:F2}  " +
            $"queries/req={queries.Average():F1}" + (compareTo is null ? "" : $"  MISMATCHES={mismatches}"));
    }

    private static bool Same(List<List<(string, int)>> a, List<List<(string, int)>> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.SequenceEqual(p.Second));

    // ---------------------------------------------------------------------------------
    private async Task SeedAsync(WebApplicationFactory<Program> f, List<CardArtCatalogEntry> catalog, Random rnd)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync();

        var tracked = catalog.Where(c => !AltArtRules.IsInfinite(c)).ToList();
        var groups = catalog.GroupBy(c => (c.FamilyId, c.Faction, c.Rarity))
            .Where(g => g.Count() > 1).ToList();
        Log($"Tracked prints: {tracked.Count}, multi-art groups: {groups.Count}");

        var sw = Stopwatch.StartNew();
        await using (var w = await conn.BeginBinaryImportAsync(
            "COPY \"Users\" (\"Id\",\"KeycloakId\",\"Role\",\"AltArtPreferenceMode\",\"CreatedAt\") FROM STDIN (FORMAT BINARY)"))
        {
            for (var u = 0; u < UserCount; u++)
            {
                await w.StartRowAsync();
                await w.WriteAsync(UserId(u));
                await w.WriteAsync($"bench-{u}");
                await w.WriteAsync("Player");
                await w.WriteAsync(u % 5 == 0 ? "Global" : "PerDeck");
                await w.WriteAsync(DateTimeOffset.UtcNow, NpgsqlDbType.TimestampTz);
            }
            await w.CompleteAsync();
        }
        await using (var w = await conn.BeginBinaryImportAsync(
            "COPY \"CardOwnerships\" (\"UserId\",\"CardReference\",\"Quantity\",\"IsUnique\") FROM STDIN (FORMAT BINARY)"))
        {
            for (var u = 0; u < UserCount; u++)
                foreach (var c in tracked.OrderBy(_ => rnd.Next()).Take(OwnedPerUser))
                {
                    await w.StartRowAsync();
                    await w.WriteAsync(UserId(u));
                    await w.WriteAsync(c.Reference);
                    await w.WriteAsync(rnd.Next(1, 4));
                    await w.WriteAsync(false);
                }
            await w.CompleteAsync();
        }
        await using (var w = await conn.BeginBinaryImportAsync(
            "COPY \"UserCardArtPreferences\" (\"UserId\",\"FamilyId\",\"Faction\",\"Rarity\",\"SlotIndex\",\"PreferredReference\") FROM STDIN (FORMAT BINARY)"))
        {
            for (var u = 0; u < UserCount; u++)
                foreach (var g in groups.OrderBy(_ => rnd.Next()).Take(GlobalPrefFamiliesPerUser))
                {
                    var max = AltArtRules.MaxSlots(g.First().CardType);
                    for (var slot = 1; slot <= max; slot++)
                    {
                        await w.StartRowAsync();
                        await w.WriteAsync(UserId(u));
                        await w.WriteAsync(g.Key.FamilyId);
                        await w.WriteAsync(g.Key.Faction);
                        await w.WriteAsync(g.Key.Rarity);
                        await w.WriteAsync(slot);
                        await w.WriteAsync(g.ElementAt(rnd.Next(g.Count())).Reference);
                    }
                }
            await w.CompleteAsync();
        }
        await using (var cmd = new NpgsqlCommand("VACUUM ANALYZE", conn)) await cmd.ExecuteNonQueryAsync();
        Log($"Seeded {UserCount} users, ~{UserCount * OwnedPerUser} ownerships, prefs in {sw.Elapsed.TotalSeconds:F1}s");
    }

    private static Guid UserId(int u) => new(u, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);

    private static List<Deck> BuildDecks(List<CardArtCatalogEntry> catalog, Random rnd)
    {
        var tracked = catalog.Where(c => !AltArtRules.IsInfinite(c) && c.CardType != "TOKEN").ToList();
        var normal = catalog.Where(c => c.CardType != "HERO" && !c.CardType.StartsWith("TOKEN")).ToList();
        return Enumerable.Range(0, UserCount).Select(u =>
        {
            var lines = new List<OwnershipCheckItem>();
            for (var i = 0; i < DeckLines; i++)
            {
                // ~60% alt-art/tracked prints, the rest base prints.
                var e = rnd.NextDouble() < .6 ? tracked[rnd.Next(tracked.Count)] : normal[rnd.Next(normal.Count)];
                lines.Add(new OwnershipCheckItem(e.Reference, rnd.Next(1, 4)));
            }
            return new Deck($"bench-{u}", u % 5 == 0, lines);
        }).ToList();
    }

    private static async Task Denormalize(OwnershipDbContext db, List<CardArtCatalogEntry> catalog)
    {
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"CardArtCatalog\" ADD COLUMN IF NOT EXISTS \"DefaultReference\" text, ADD COLUMN IF NOT EXISTS \"IsInfinite\" boolean, ADD COLUMN IF NOT EXISTS \"IsToken\" boolean");
        var snap = new CatalogSnapshot(catalog);
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE \"CardArtCatalog\" c SET \"DefaultReference\"=v.d, \"IsInfinite\"=v.i, \"IsToken\"=v.t FROM unnest(@r,@d,@i,@t) AS v(r,d,i,t) WHERE c.\"Reference\"=v.r", conn);
        cmd.Parameters.AddWithValue("r", snap.ByRef.Keys.ToArray());
        cmd.Parameters.AddWithValue("d", snap.ByRef.Values.Select(e => snap.DefaultOf(e)).ToArray());
        cmd.Parameters.AddWithValue("i", snap.ByRef.Values.Select(e => snap.IsInfinite(e)).ToArray());
        cmd.Parameters.AddWithValue("t", snap.ByRef.Values.Select(e => e.IsTokenEntry()).ToArray());
        await cmd.ExecuteNonQueryAsync();
        await using var idx = new NpgsqlCommand("ANALYZE \"CardArtCatalog\"", conn);
        await idx.ExecuteNonQueryAsync();
    }

    private static string JoinSql(bool byUserId) => $"""
        SELECT c."Reference", c."DefaultReference", c."IsInfinite", COALESCE(o."Quantity", 0) AS "Owned", u."Id" AS "UserId"
        FROM "Users" u
        CROSS JOIN "CardArtCatalog" c
        LEFT JOIN "CardOwnerships" o ON o."UserId" = u."Id" AND o."CardReference" = c."Reference"
        WHERE {(byUserId ? "u.\"Id\" = @u" : "u.\"KeycloakId\" = @u")} AND c."Reference" = ANY(@refs)
        """;

    // Tokens: the only thing besides the deck lines that needs the user's preferences.
    private static Task<List<string>> LoadTokenPrefs(OwnershipDbContext db, Guid userId) =>
        db.Database.SqlQueryRaw<string>("""
            SELECT p."PreferredReference" AS "Value" FROM "UserCardArtPreferences" p
            JOIN "CardArtCatalog" c ON c."Reference" = p."PreferredReference"
            WHERE p."UserId" = @u AND p."SlotIndex" = 1 AND c."IsToken"
            """, new NpgsqlParameter("u", userId)).ToListAsync();

    // Result of the join query.
    public record JoinRow(string Reference, string DefaultReference, bool IsInfinite, int Owned, Guid UserId);

    // Immutable copy of CardArtCatalog with the derived facts precomputed.
    public sealed class CatalogSnapshot
    {
        public Dictionary<string, CardArtCatalogEntry> ByRef { get; }
        private readonly Dictionary<(int, string, string), string> _default;
        private readonly Dictionary<string, bool> _infinite = new();

        public CatalogSnapshot(IEnumerable<CardArtCatalogEntry> rows)
        {
            var list = rows.ToList();
            ByRef = list.ToDictionary(r => r.Reference);
            foreach (var r in list) _infinite[r.Reference] = AltArtRules.IsInfinite(r);
            _default = list.GroupBy(r => (r.FamilyId, r.Faction, r.Rarity))
                .ToDictionary(g => g.Key, g => ResolveDefault(g.ToList()).Reference);
        }

        public static async Task<CatalogSnapshot> LoadAsync(OwnershipDbContext db) =>
            new(await db.CardArtCatalog.AsNoTracking().ToListAsync());

        public bool IsInfinite(CardArtCatalogEntry e) => _infinite[e.Reference];

        public string DefaultOf(CardArtCatalogEntry e) => _default[(e.FamilyId, e.Faction, e.Rarity)];

        // Same rule as AltArtService.ResolveDefaultRow (private there).
        private static CardArtCatalogEntry ResolveDefault(List<CardArtCatalogEntry> g)
        {
            var untracked = g.Where(r => !CardReferenceParser.IsAlternateArt(r.Reference)).ToList();
            if (untracked.Count > 0) return untracked.OrderBy(r => r.SortOrder).First();
            var nonPromo = g.Where(r => !r.IsPromo).ToList();
            return (nonPromo.Count > 0 ? nonPromo : g).OrderBy(r => r.SortOrder).First();
        }
    }

    // PerDeck rule: an unowned tracked print falls back to the group's default print.
    private static class PerDeck
    {
        public static List<List<(string, int)>> Resolve(
            List<OwnershipCheckItem> deck, Func<string, CardArtCatalogEntry?> lookup, CatalogSnapshot snap,
            Dictionary<string, int> owned) =>
            Core(deck, r => lookup(r) is { } e ? (snap.DefaultOf(e), snap.IsInfinite(e)) : null, owned);

        public static List<List<(string, int)>> ResolveFromRows(List<OwnershipCheckItem> deck, List<JoinRow> rows)
        {
            var byRef = rows.ToDictionary(r => r.Reference);
            var owned = rows.Where(r => r.Owned > 0).ToDictionary(r => r.Reference, r => r.Owned);
            return Core(deck, r => byRef.TryGetValue(r, out var x) ? (x.DefaultReference, x.IsInfinite) : null, owned);
        }

        private static List<List<(string, int)>> Core(
            List<OwnershipCheckItem> deck, Func<string, (string Default, bool Infinite)?> info, Dictionary<string, int> owned)
        {
            var remaining = new Dictionary<string, int>(owned);
            var lines = new List<List<(string, int)>>(deck.Count);
            foreach (var item in deck)
            {
                if (info(item.Reference) is not { } e || e.Infinite || item.Reference == e.Default)
                {
                    lines.Add([(item.Reference, item.Quantity)]);
                    continue;
                }
                var ownedQty = remaining.GetValueOrDefault(item.Reference, 0);
                remaining[item.Reference] = Math.Max(0, ownedQty - item.Quantity);
                if (ownedQty >= item.Quantity) { lines.Add([(item.Reference, item.Quantity)]); continue; }
                var line = new List<(string, int)>();
                if (ownedQty > 0) line.Add((item.Reference, ownedQty));
                line.Add((e.Default, item.Quantity - ownedQty));
                lines.Add(line);
            }
            return lines;
        }
    }
}

public static class CatalogSnapshotExtensions
{
    public static bool IsTokenEntry(this CardArtCatalogEntry e) => e.CardType.StartsWith("TOKEN", StringComparison.Ordinal);
}
